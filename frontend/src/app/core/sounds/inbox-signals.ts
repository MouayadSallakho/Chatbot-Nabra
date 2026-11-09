import { HttpClient } from '@angular/common/http';
import {
  DOCUMENT,
  EnvironmentInjector,
  Injectable,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import type {
  ConversationCustomer,
  ConversationPage,
  ConversationSummary,
  TranscriptEvent,
  TranscriptPage,
} from '@app/features/conversations/conversations-api';
import { customerLabelPlain } from '@app/features/conversations/customer-label';
import { ReadPacer, SignalDecision, decideSignals } from './inbox-signal-rules';
import type { SoundName, SoundRequest } from './sound-names';
import { SOUND_PLATFORM, SoundPlatform } from './sound-platform';
import { SoundSettings } from './sound-settings';
import { Sounds } from './sounds';

/** How long this browser's own action keeps its conversation silent. */
export const OWN_ACTION_MS = 5_000;

/** How many failed turns are remembered, so none is told twice. */
const SEEN_LIMIT = 200;

const FAILED_TURN = 'AiTurnFailed';

/** Everything a tab needs to take over from the one that was reading. */
interface Snapshot {
  readonly rows: readonly (readonly [string, string])[];
  readonly seen: readonly string[];
}

interface TabState {
  readonly showing: boolean;
  readonly viewing: string | null;
  readonly visible: boolean;
}

type SignalMessage =
  | { readonly kind: 'hello' }
  | ({ readonly kind: 'tab'; readonly tab: string } & TabState)
  | { readonly kind: 'gone'; readonly tab: string }
  | { readonly kind: 'own'; readonly conversationId: string }
  | {
      readonly kind: 'result';
      readonly total: number;
      readonly snapshot: Snapshot;
      readonly sound: SoundRequest | null;
    };

/**
 * What the inbox is signalling, for every tab of this browser.
 *
 * <strong>One reader per browser and workspace.</strong> The hub sends only an id,
 * so the needs-a-person count and the sounds come from one coalesced read of that
 * set: at most every five seconds, by the one tab holding a long-lived Web Lock. The
 * other tabs are told the result over a channel and read nothing. The lock also
 * lowers the chance a hidden tab is put to sleep, which is when sounds matter most.
 *
 * <strong>It reads only when somebody will use it:</strong> while sounds or desktop
 * alerts are on, or a tab of this workspace shows the count.
 *
 * <strong>A sound is never the only signal.</strong> The count, the unread dots and
 * the dividers show the same facts on screen, and this browser's own actions are
 * silent.
 */
@Injectable({ providedIn: 'root' })
export class InboxSignals {
  private readonly injector = inject(EnvironmentInjector);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly settings = inject(SoundSettings);

  private readonly localShowing = signal(0);
  private readonly othersShowing = signal(0);
  private readonly viewingId = signal<string | null>(null);

  private started = false;
  private session: SignalSession | null = null;

  /** How many conversations need a person; null until it is known. */
  readonly count = signal<number | null>(null);

  private readonly audible = computed(
    () => this.settings.enabled() || this.settings.desktopAlerts(),
  );

  private readonly wanted = computed(
    () => this.audible() || this.localShowing() > 0 || this.othersShowing() > 0,
  );

  /** Begins following the current workspace. Safe to repeat. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;

    effect(
      () => {
        const tenantId = this.workspace.tenantId();
        const allowed =
          tenantId !== null &&
          this.permissions.tenantId() === tenantId &&
          this.permissions.has('conversations.view');

        untracked(() => this.attach(allowed ? tenantId : null));
      },
      { injector: this.injector },
    );

    effect(
      () => {
        const wanted = this.wanted();

        untracked(() => this.session?.setWanted(wanted));
      },
      { injector: this.injector },
    );

    effect(
      () => {
        this.localShowing();
        this.viewingId();

        untracked(() => this.session?.postTab());
      },
      { injector: this.injector },
    );
  }

  /** The count is on screen. Returns what to call when it no longer is. */
  showCount(): () => void {
    // Screens ask from inside an effect, and an effect cannot be created in one.
    untracked(() => this.start());
    this.localShowing.update((count) => count + 1);

    return () => this.localShowing.update((count) => count - 1);
  }

  /** Which conversation this tab has open, or null. */
  viewing(conversationId: string | null): void {
    this.viewingId.set(conversationId);
  }

  /** This browser is about to act on a conversation: that is not news to it. */
  ownAction(conversationId: string): void {
    this.session?.recordOwn(conversationId);
  }

  /**
   * What this tab's own transcript read found.
   *
   * `live` is false for a first read of a conversation: its failures are history,
   * remembered but never told.
   */
  transcriptRead(
    conversation: { readonly id: string; readonly customer: ConversationCustomer | null },
    events: readonly TranscriptEvent[],
    live: boolean,
  ): void {
    this.session?.noteTranscript(conversation, events, live);
  }

  /** What a session needs from this service. */
  private readonly host: SessionHost = {
    http: inject(HttpClient),
    auth: inject(AuthService),
    realtime: inject(ConversationRealtime),
    sounds: inject(Sounds),
    platform: inject(SOUND_PLATFORM),
    document: inject(DOCUMENT),
    audible: () => this.audible(),
    showing: () => this.localShowing() > 0,
    viewing: () => this.viewingId(),
    setCount: (total) => this.count.set(total),
    setOthersShowing: (count) => this.othersShowing.set(count),
  };

  private attach(tenantId: string | null): void {
    if (this.session?.tenantId === (tenantId ?? undefined)) {
      return;
    }

    this.session?.close();
    this.session = null;
    this.othersShowing.set(0);
    this.count.set(null);

    if (tenantId !== null) {
      this.session = new SignalSession(tenantId, this.host);
      this.session.setWanted(this.wanted());
      this.session.begin();
    }
  }
}

interface SessionHost {
  readonly http: HttpClient;
  readonly auth: AuthService;
  readonly realtime: ConversationRealtime;
  readonly sounds: Sounds;
  readonly platform: SoundPlatform;
  readonly document: Document;
  audible(): boolean;
  showing(): boolean;
  viewing(): string | null;
  setCount(total: number): void;
  setOthersShowing(count: number): void;
}

/** The words of a desktop alert: the reason, never transcript text. */
function reason(sound: SoundName): string {
  switch (sound) {
    case 'ai-failed':
      return $localize`:@@transcript.event.aiFailed:The AI could not reply`;
    case 'needs-person':
      return $localize`:@@inbox.view.needsPerson:Needs a person`;
    default:
      return $localize`:@@inbox.alert.newMessage:New message`;
  }
}

function request(
  sound: SoundName,
  conversation: { readonly id: string; readonly customer: ConversationCustomer | null },
): SoundRequest {
  return {
    sound,
    alert: {
      conversationId: conversation.id,
      title: customerLabelPlain(conversation.customer),
      body: reason(sound),
    },
  };
}

/** One workspace's signals, in this tab. */
class SignalSession {
  private readonly id = `${Date.now()}-${Math.random()}`;
  private readonly pacer = new ReadPacer(() => this.read());
  private readonly tabs = new Map<string, TabState>();
  private readonly own = new Map<string, number>();

  private channel: BroadcastChannel | null = null;
  private abort: AbortController | null = null;
  private releaseLock: (() => void) | null = null;
  private stopListening: (() => void) | null = null;

  private reader = false;
  private closed = false;
  private wanted = false;

  private previous: Map<string, string> | null = null;
  private seen = new Set<string>();
  private total: number | null = null;

  private readonly onVisibility = (): void => this.postTab();
  private readonly onPageHide = (): void => this.post({ kind: 'gone', tab: this.id });

  constructor(
    readonly tenantId: string,
    private readonly host: SessionHost,
  ) {}

  private get name(): string {
    return `nabra:inbox-signals:${this.tenantId}`;
  }

  begin(): void {
    this.openChannel();
    this.post({ kind: 'hello' });
    this.postTab();
    this.host.document.addEventListener('visibilitychange', this.onVisibility);
    this.host.document.defaultView?.addEventListener('pagehide', this.onPageHide);

    this.stopListening = this.host.realtime.listen(this.tenantId, () => {
      if (this.reader && this.wanted) {
        this.pacer.event();
      }
    });

    this.requestLock();
  }

  close(): void {
    this.closed = true;
    this.onPageHide();
    this.pacer.stop();
    this.stopListening?.();
    this.host.document.removeEventListener('visibilitychange', this.onVisibility);
    this.host.document.defaultView?.removeEventListener('pagehide', this.onPageHide);
    this.abort?.abort();
    this.releaseLock?.();
    this.channel?.close();
    this.channel = null;
  }

  setWanted(wanted: boolean): void {
    const was = this.wanted;

    this.wanted = wanted;

    if (!wanted) {
      // Nobody was following: what changed meanwhile is not news when they return.
      this.previous = null;
    } else if (!was && this.reader) {
      this.pacer.soon();
    }
  }

  postTab(): void {
    this.post({
      kind: 'tab',
      tab: this.id,
      showing: this.host.showing(),
      viewing: this.host.viewing(),
      visible: this.host.platform.isVisible(),
    });
  }

  recordOwn(conversationId: string): void {
    this.own.set(conversationId, Date.now());
    this.post({ kind: 'own', conversationId });
  }

  /** Only the reading tab acts on this: the others are told by its result. */
  noteTranscript(
    conversation: { readonly id: string; readonly customer: ConversationCustomer | null },
    events: readonly TranscriptEvent[],
    live: boolean,
  ): void {
    if (!this.reader) {
      return;
    }

    const failed = events.filter((event) => event.kind === FAILED_TURN);
    const fresh = failed.some((event) => !this.seen.has(event.id));

    this.remember(failed);

    if (live && fresh && this.host.audible() && !this.ownSince(Date.now()).has(conversation.id)) {
      this.tell(request('ai-failed', conversation));
    }
  }

  private requestLock(): void {
    const locks = this.host.platform.locks;

    if (!locks) {
      this.becomeReader();

      return;
    }

    this.abort = new AbortController();

    const held = new Promise<void>((resolve) => (this.releaseLock = resolve));

    locks
      .request(this.name, { signal: this.abort.signal }, () => {
        if (!this.closed) {
          this.becomeReader();
        }

        return held;
      })
      .catch(() => {
        // A lock manager that refuses (an opaque origin) leaves this tab alone.
        if (!this.closed) {
          this.becomeReader();
        }
      });
  }

  private becomeReader(): void {
    this.reader = true;
    // The other tabs say what they show and what they know.
    this.post({ kind: 'hello' });

    if (this.wanted) {
      this.pacer.soon();
    }
  }

  private async read(): Promise<void> {
    if (!this.wanted) {
      return;
    }

    const startedAt = Date.now();
    let page: ConversationPage;

    try {
      page = await firstValueFrom(
        this.host.http.get<ConversationPage>(
          `/api/tenants/${this.tenantId}/conversations?filter=needs-person&page=1&pageSize=50`,
        ),
      );
    } catch (error) {
      const seconds = toApiError(error).retryAfterSeconds;

      if (seconds !== undefined) {
        this.pacer.hold(seconds * 1000);
      }

      return;
    }

    if (this.closed) {
      return;
    }

    const decision =
      this.previous !== null && this.host.audible()
        ? decideSignals({
            previous: this.previous,
            rows: page.items,
            me: this.host.auth.user()?.id ?? null,
            viewing: this.viewingNow(),
            own: this.ownSince(startedAt),
            openHere: this.host.viewing(),
          })
        : null;

    this.previous = new Map(page.items.map((row) => [row.id, row.lastActivityAt]));

    const sound = decision === null ? null : await this.choose(decision);

    if (this.closed) {
      return;
    }

    this.total = page.total;
    this.host.setCount(page.total);
    this.publish(sound);

    if (sound !== null) {
      this.host.sounds.request(sound);
    }
  }

  /** The most important thing to tell: a failed turn, then an entry, then a new message. */
  private async choose(decision: SignalDecision): Promise<SoundRequest | null> {
    const failed = await this.findFailure(decision.failureChecks);

    if (failed !== null) {
      return request('ai-failed', failed);
    }

    if (decision.entered !== null) {
      return request('needs-person', decision.entered);
    }

    return decision.newMessage !== null ? request('new-message', decision.newMessage) : null;
  }

  /** One small read per changed AI-handled conversation; the first fresh failure wins. */
  private async findFailure(
    rows: readonly ConversationSummary[],
  ): Promise<ConversationSummary | null> {
    const results = await Promise.all(
      rows.map(async (row) => {
        try {
          const page = await firstValueFrom(
            this.host.http.get<TranscriptPage>(
              `/api/tenants/${this.tenantId}/conversations/${row.id}/messages?tail=true&limit=1&include=events`,
            ),
          );

          return { row, failed: (page.events ?? []).filter((e) => e.kind === FAILED_TURN) };
        } catch {
          // A sound is never the only signal: the row is on screen anyway.
          return { row, failed: [] };
        }
      }),
    );

    let found: ConversationSummary | null = null;

    for (const { row, failed } of results) {
      if (found === null && failed.some((event) => !this.seen.has(event.id))) {
        found = row;
      }

      this.remember(failed);
    }

    return found;
  }

  private remember(events: readonly TranscriptEvent[]): void {
    for (const event of events) {
      this.seen.add(event.id);
    }

    while (this.seen.size > SEEN_LIMIT) {
      this.seen.delete(this.seen.values().next().value as string);
    }
  }

  private tell(sound: SoundRequest): void {
    this.publish(sound);
    this.host.sounds.request(sound);
  }

  private publish(sound: SoundRequest | null): void {
    if (this.total !== null) {
      this.post({ kind: 'result', total: this.total, snapshot: this.snapshot(), sound });
    }
  }

  private snapshot(): Snapshot {
    return { rows: [...(this.previous ?? [])], seen: [...this.seen] };
  }

  private viewingNow(): ReadonlySet<string> {
    const here: TabState = {
      showing: this.host.showing(),
      viewing: this.host.viewing(),
      visible: this.host.platform.isVisible(),
    };

    return new Set(
      [here, ...this.tabs.values()]
        .filter((tab) => tab.visible && tab.viewing !== null)
        .map((tab) => tab.viewing as string),
    );
  }

  /** Measured from when the read started, so a slow read cannot outlast an own action. */
  private ownSince(at: number): ReadonlySet<string> {
    for (const [id, when] of this.own) {
      if (when < at - OWN_ACTION_MS) {
        this.own.delete(id);
      }
    }

    return new Set(this.own.keys());
  }

  private openChannel(): void {
    try {
      this.channel = this.host.platform.createChannel(this.name);
    } catch {
      this.channel = null;
    }

    if (this.channel !== null) {
      this.channel.onmessage = ({ data }: MessageEvent<SignalMessage>) => this.receive(data);
    }
  }

  private receive(message: SignalMessage): void {
    switch (message?.kind) {
      case 'hello':
        this.postTab();
        this.publishIfReader();
        break;
      case 'tab':
        this.tabs.set(message.tab, message);
        this.countOthers();
        break;
      case 'gone':
        this.tabs.delete(message.tab);
        this.countOthers();
        break;
      case 'own':
        this.own.set(message.conversationId, Date.now());
        break;
      case 'result':
        this.adopt(message);
        break;
    }
  }

  private publishIfReader(): void {
    if (this.reader) {
      this.publish(null);
    }
  }

  private countOthers(): void {
    this.host.setOthersShowing([...this.tabs.values()].filter((tab) => tab.showing).length);
  }

  /** Another tab read. A tab that later inherits the lock starts from what it heard here. */
  private adopt(message: Extract<SignalMessage, { kind: 'result' }>): void {
    if (this.reader) {
      return;
    }

    this.total = message.total;
    this.previous = new Map(message.snapshot.rows);
    this.seen = new Set(message.snapshot.seen);
    this.host.setCount(message.total);

    if (message.sound !== null) {
      this.host.sounds.request(message.sound);
    }
  }

  private post(message: SignalMessage): void {
    try {
      this.channel?.postMessage(message);
    } catch {
      // A channel that has gone away leaves this tab to work alone.
    }
  }
}
