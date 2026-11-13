import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT, effect, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '@app/core/auth/auth-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { InboxSignals, OWN_ACTION_MS } from './inbox-signals';
import { READ_GAP_MS } from './inbox-signal-rules';
import { SOUND_PLATFORM, SoundPlatform } from './sound-platform';
import { SoundSettings } from './sound-settings';
import { Sounds } from './sounds';

const TENANT = 't1';
const READ = `/api/tenants/${TENANT}/conversations?filter=needs-person&page=1&pageSize=50`;
const events = (id: string) =>
  `/api/tenants/${TENANT}/conversations/${id}/messages?tail=true&limit=1&include=events`;
const NAME = `nabra:inbox-signals:${TENANT}`;

const OLD = '2026-11-08T10:00:00Z';
const NEW = '2026-11-08T10:05:00Z';
const me = { userId: 'me', email: 'me@example.test' };

const row = (id: string, over: object = {}) => ({
  id,
  handling: 'Human',
  lastActivityAt: OLD,
  lastMessageSender: 'Customer',
  messageCount: 1,
  assignedTo: null,
  takenOverBy: null,
  customer: {
    id: `p-${id}`,
    displayName: null,
    labelCode: 'AnonymousVisitor',
    reference: null,
    shortCode: '7KQ2MX',
  },
  ...over,
});

/** Every channel of one name hears the others, as in a browser; never itself. */
class FakeChannel {
  static all: FakeChannel[] = [];

  onmessage: ((event: MessageEvent) => void) | null = null;
  closed = false;

  constructor(readonly name: string) {
    FakeChannel.all.push(this);
  }

  postMessage(data: unknown): void {
    for (const other of FakeChannel.all) {
      if (other !== this && other.name === this.name && !other.closed) {
        other.onmessage?.({ data: structuredClone(data) } as MessageEvent);
      }
    }
  }

  close(): void {
    this.closed = true;
  }
}

/** One holder per name; the next in line gets it when the holder lets go. */
function fakeLocks(held = false): LockManager & { hold(): void } {
  let taken = held;
  const line: (() => void)[] = [];

  const next = (): void => {
    const waiting = line.shift();

    if (waiting) {
      waiting();
    } else {
      taken = false;
    }
  };

  return {
    hold: () => next(),
    request: (_name: string, _options: LockOptions, callback: LockGrantedCallback<unknown>) =>
      new Promise((resolve) => {
        const grant = (): void => {
          taken = true;
          void Promise.resolve(callback({ mode: 'exclusive', name: _name } as Lock)).then(() => {
            next();
            resolve(undefined);
          });
        };

        if (taken) {
          line.push(grant);
        } else {
          grant();
        }
      }),
  } as unknown as LockManager & { hold(): void };
}

describe('InboxSignals', () => {
  let http: HttpTestingController;
  let service: InboxSignals;
  let requested: ReturnType<typeof vi.fn>;
  let locks: ReturnType<typeof fakeLocks>;
  let visible: boolean;
  let notifyChanged: (id: string | null) => void;
  let tenant: ReturnType<typeof signal<string | null>>;
  let enabled: ReturnType<typeof signal<boolean>>;

  /** Stands in for another tab of the same browser. */
  let otherTab: FakeChannel;

  function create(options: { held?: boolean; audible?: boolean } = {}): void {
    FakeChannel.all = [];
    otherTab = new FakeChannel(NAME);
    locks = fakeLocks(options.held);
    visible = true;
    requested = vi.fn();
    tenant = signal<string | null>(TENANT);
    enabled = signal(options.audible ?? true);

    const platform: Partial<SoundPlatform> = {
      locks,
      createChannel: (name) => new FakeChannel(name) as unknown as BroadcastChannel,
      isVisible: () => visible,
      hasFocus: () => visible,
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SOUND_PLATFORM, useValue: platform },
        { provide: Sounds, useValue: { request: requested } },
        { provide: AuthService, useValue: { user: () => ({ id: 'me' }) } },
        {
          provide: ConversationRealtime,
          useValue: {
            listen: (_tenant: string, callback: (id: string | null) => void) => {
              notifyChanged = callback;

              return () => undefined;
            },
          },
        },
        { provide: WorkspaceContextService, useValue: { tenantId: tenant } },
        {
          provide: PermissionService,
          useValue: { tenantId: () => TENANT, has: () => true },
        },
        {
          provide: SoundSettings,
          useValue: { enabled, desktopAlerts: signal(false) },
        },
        { provide: DOCUMENT, useValue: document },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(InboxSignals);
  }

  /** Starts the service, up to its first read being out. */
  async function start(): Promise<void> {
    service.start();
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);
  }

  const page = (items: object[], total = items.length) => ({
    items,
    page: 1,
    pageSize: 50,
    total,
  });

  async function answer(items: object[], total?: number): Promise<void> {
    http.expectOne(READ).flush(page(items, total));
    await vi.advanceTimersByTimeAsync(0);
  }

  /** The first read: the baseline. */
  async function baseline(items: object[] = []): Promise<void> {
    await start();
    await answer(items);
  }

  /** One event, to the moment its read is out. */
  async function changed(): Promise<void> {
    await vi.advanceTimersByTimeAsync(READ_GAP_MS);
    notifyChanged('anything');
    await vi.advanceTimersByTimeAsync(READ_GAP_MS);
  }

  const sounds = () => requested.mock.calls.map(([request]) => request.sound);

  beforeEach(() => vi.useFakeTimers());

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  describe('the first read', () => {
    it('is the baseline: it plays nothing, whatever is in the set', async () => {
      create();
      await baseline([row('a'), row('b', { handling: 'Ai' })]);

      expect(sounds()).toEqual([]);
    });

    it('gives the count', async () => {
      create();
      await start();
      await answer([row('a')], 125);

      expect(service.count()).toBe(125);
    });
  });

  describe('needs a person', () => {
    it('plays when a conversation enters the set, with who and why for an alert', async () => {
      create();
      await baseline([row('a')]);

      await changed();
      await answer([row('b', { lastActivityAt: NEW }), row('a')]);

      expect(requested).toHaveBeenCalledWith({
        sound: 'needs-person',
        alert: { conversationId: 'b', title: 'Visitor 7KQ2MX', body: 'Needs a person' },
      });
    });

    it('never puts transcript text in an alert', async () => {
      create();
      await baseline();

      await changed();
      await answer([row('a', { lastMessagePreview: 'A private message' })]);

      expect(JSON.stringify(requested.mock.calls)).not.toContain('A private message');
    });

    it('updates the count on every read, with no read per event', async () => {
      create();
      await baseline([row('a')]);

      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      notifyChanged('x');
      notifyChanged('y');
      notifyChanged('z');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await answer([row('a'), row('b')], 2);

      expect(service.count()).toBe(2);
    });
  });

  describe('a new message', () => {
    const mine = { assignedTo: me };

    it('plays for my conversation that I am not viewing', async () => {
      create();
      await baseline([row('a', mine)]);

      await changed();
      await answer([row('a', { ...mine, lastActivityAt: NEW, messageCount: 2 })]);

      expect(sounds()).toEqual(['new-message']);
    });

    it('stays silent while it is open and the tab is visible', async () => {
      create();
      service.viewing('a');
      await baseline([row('a', mine)]);

      await changed();
      await answer([row('a', { ...mine, lastActivityAt: NEW, messageCount: 2 })]);

      expect(sounds()).toEqual([]);
    });

    it('plays when it is open but the tab is hidden', async () => {
      create();
      service.viewing('a');
      await baseline([row('a', mine)]);
      visible = false;

      await changed();
      await answer([row('a', { ...mine, lastActivityAt: NEW, messageCount: 2 })]);

      expect(sounds()).toEqual(['new-message']);
    });

    it('stays silent when another visible tab has it open', async () => {
      create();
      await baseline([row('a', mine)]);
      otherTab.postMessage({
        kind: 'tab',
        tab: 'other',
        showing: false,
        viewing: 'a',
        visible: true,
      });

      await changed();
      await answer([row('a', { ...mine, lastActivityAt: NEW, messageCount: 2 })]);

      expect(sounds()).toEqual([]);
    });

    it('plays for a conversation I took over, which is not assigned to me (BA-2)', async () => {
      const takenByMe = { takenOverBy: me };

      create();
      await baseline([row('a', takenByMe)]);

      await changed();
      await answer([row('a', { ...takenByMe, lastActivityAt: NEW, messageCount: 2 })]);

      expect(sounds()).toEqual(['new-message']);
    });

    it('plays once for a message, however many times it is announced', async () => {
      create();
      await baseline([row('a', mine)]);

      // Stored: the customer's message.
      await changed();
      await answer([row('a', { ...mine, lastActivityAt: NEW, messageCount: 2 })]);

      // Settled, and then an assignment: the row moves twice more, with no new message.
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await changed();
      await answer([
        row('a', { ...mine, lastActivityAt: '2026-11-08T10:05:11Z', messageCount: 2 }),
      ]);
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await changed();
      await answer([
        row('a', { ...mine, lastActivityAt: '2026-11-08T10:06:00Z', messageCount: 2 }),
      ]);

      expect(sounds()).toEqual(['new-message']);
    });
  });

  describe('a failed AI turn', () => {
    const failure = (id: string) => ({ kind: 'AiTurnFailed', id, actor: null });
    const ai = { handling: 'Ai' };

    it('plays when the transcript of a new AI-handled row has a failure', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(page([row('a', ai)]));
      await vi.advanceTimersByTimeAsync(0);
      http.expectOne(events('a')).flush({ items: [], events: [failure('e1')] });
      await vi.advanceTimersByTimeAsync(0);

      expect(requested).toHaveBeenCalledWith({
        sound: 'ai-failed',
        alert: { conversationId: 'a', title: 'Visitor 7KQ2MX', body: 'The AI could not reply' },
      });
    });

    it('never tells the same failure twice', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(page([row('a', ai)]));
      await vi.advanceTimersByTimeAsync(0);
      http.expectOne(events('a')).flush({ items: [], events: [failure('e1')] });
      await vi.advanceTimersByTimeAsync(0);

      await changed();
      http.expectOne(READ).flush(page([row('a', { ...ai, lastActivityAt: NEW })]));
      await vi.advanceTimersByTimeAsync(0);
      http.expectOne(events('a')).flush({ items: [], events: [failure('e1')] });
      await vi.advanceTimersByTimeAsync(0);

      expect(sounds()).toEqual(['ai-failed']);
    });

    it('outranks a conversation entering the set', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(page([row('b'), row('a', ai)]));
      await vi.advanceTimersByTimeAsync(0);
      http.expectOne(events('a')).flush({ items: [], events: [failure('e1')] });
      await vi.advanceTimersByTimeAsync(0);

      expect(sounds()).toEqual(['ai-failed']);
    });

    it('makes at most three small reads per read', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(page(['a', 'b', 'c', 'd', 'e'].map((id) => row(id, ai))));
      await vi.advanceTimersByTimeAsync(0);

      const reads = http.match(() => true);

      expect(reads).toHaveLength(3);
      reads.forEach((read) => read.flush({ items: [], events: [] }));
      await vi.advanceTimersByTimeAsync(0);
    });

    it('survives a small read that fails, and still tells the rest', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(page([row('a', ai)]));
      await vi.advanceTimersByTimeAsync(0);
      http
        .expectOne(events('a'))
        .flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
      await vi.advanceTimersByTimeAsync(0);

      expect(sounds()).toEqual(['needs-person']);
    });

    it('uses the transcript this tab already read for the conversation it has open', async () => {
      create();
      service.viewing('a');
      await baseline();

      await changed();
      // No small read for "a": verify() in afterEach proves it.
      await answer([row('a', ai)]);

      service.transcriptRead({ id: 'a', customer: null }, [failure('e1') as never], true);

      expect(requested).toHaveBeenCalledWith({
        sound: 'ai-failed',
        alert: { conversationId: 'a', title: 'Customer', body: 'The AI could not reply' },
      });
    });

    it('tells nothing for failures found by opening an old conversation', async () => {
      create();
      await baseline();

      service.transcriptRead({ id: 'a', customer: null }, [failure('e1') as never], false);

      expect(sounds()).toEqual([]);
    });

    it('makes no small reads when nobody can hear them', async () => {
      create({ audible: false });
      const release = service.showCount();

      await start();
      await answer([]);

      await changed();
      await answer([row('a', ai)]);

      expect(sounds()).toEqual([]);
      release();
    });
  });

  describe("this browser's own actions", () => {
    it('are silent', async () => {
      create();
      await baseline();
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);

      service.ownAction('a');
      notifyChanged('a');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await answer([row('a')]);

      expect(sounds()).toEqual([]);
    });

    it('are silent when another tab did them too', async () => {
      create();
      await baseline();
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);

      otherTab.postMessage({ kind: 'own', conversationId: 'a' });
      notifyChanged('a');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await answer([row('a')]);

      expect(sounds()).toEqual([]);
    });

    it('stop counting after five seconds', async () => {
      create();
      await baseline();

      service.ownAction('a');
      await vi.advanceTimersByTimeAsync(OWN_ACTION_MS + READ_GAP_MS + 1);
      notifyChanged('a');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await answer([row('a')]);

      expect(sounds()).toEqual(['needs-person']);
    });
  });

  describe('one reader per browser and workspace', () => {
    it('reads nothing while another tab holds the lock, and uses what that tab says', async () => {
      create({ held: true });
      await start();

      notifyChanged('a');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS * 2);

      otherTab.postMessage({
        kind: 'result',
        total: 7,
        snapshot: { rows: [], seen: [] },
        sound: { sound: 'needs-person' },
      });

      expect(service.count()).toBe(7);
      expect(sounds()).toEqual(['needs-person']);
    });

    it('asks the other tabs what they know when it opens', async () => {
      create({ held: true });

      const heard: unknown[] = [];

      otherTab.onmessage = ({ data }) => heard.push(data);
      await start();

      expect(heard).toContainEqual({ kind: 'hello' });
    });

    it('tells the other tabs each result', async () => {
      create();

      const heard: { kind: string; total?: number }[] = [];

      otherTab.onmessage = ({ data }) => heard.push(data);
      await baseline([row('a')]);

      expect(heard.filter((message) => message.kind === 'result').map((m) => m.total)).toEqual([1]);
    });

    it('starts from what it heard when it inherits the lock, so no new baseline', async () => {
      create({ held: true });
      await start();

      otherTab.postMessage({
        kind: 'result',
        total: 1,
        snapshot: { rows: [['a', { at: OLD, count: 1 }]], seen: [] },
        sound: null,
      });

      // The tab that was reading closes.
      locks.hold();
      await vi.advanceTimersByTimeAsync(0);

      // What it inherited is compared, not recorded: "b" is news, "a" is not.
      await answer([row('a'), row('b')], 2);

      expect(sounds()).toEqual(['needs-person']);
    });

    it('does not tell the same failure after a handover', async () => {
      create({ held: true });
      await start();

      otherTab.postMessage({
        kind: 'result',
        total: 0,
        snapshot: { rows: [], seen: ['e1'] },
        sound: null,
      });
      locks.hold();
      await vi.advanceTimersByTimeAsync(0);

      http.expectOne(READ).flush(page([row('a', { handling: 'Ai' })]));
      await vi.advanceTimersByTimeAsync(0);
      http
        .expectOne(events('a'))
        .flush({ items: [], events: [{ kind: 'AiTurnFailed', id: 'e1', actor: null }] });
      await vi.advanceTimersByTimeAsync(0);

      expect(sounds()).toEqual(['needs-person']);
    });
  });

  describe('when it reads', () => {
    it('reads nothing with sounds off and no count on screen', async () => {
      create({ audible: false });
      await start();

      notifyChanged('a');
      await vi.advanceTimersByTimeAsync(READ_GAP_MS * 2);

      expect(service.count()).toBeNull();
    });

    it('reads for a count on screen, with sounds off', async () => {
      create({ audible: false });
      const release = service.showCount();

      await start();
      await answer([row('a')]);

      expect(service.count()).toBe(1);
      release();
    });

    it('reads when another tab shows the count, with sounds off', async () => {
      create({ audible: false });
      await start();

      otherTab.postMessage({
        kind: 'tab',
        tab: 'other',
        showing: true,
        viewing: null,
        visible: true,
      });
      TestBed.tick();
      await vi.advanceTimersByTimeAsync(0);

      await answer([row('a')]);
      expect(service.count()).toBe(1);
    });

    it('starts over when sounds come back on, so what changed meanwhile is not news', async () => {
      create();
      await baseline([row('a')]);

      enabled.set(false);
      TestBed.tick();
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);

      enabled.set(true);
      TestBed.tick();
      await vi.advanceTimersByTimeAsync(READ_GAP_MS);
      await answer([row('a'), row('b')], 2);

      expect(sounds()).toEqual([]);
    });

    it('waits as long as the server says after a 429', async () => {
      create();
      await baseline();

      await changed();
      http.expectOne(READ).flush(
        { title: 'Too many' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: { 'Retry-After': '60' },
        },
      );
      await vi.advanceTimersByTimeAsync(0);

      await changed();
      http.expectNone(READ);

      await vi.advanceTimersByTimeAsync(60_000);
      await answer([]);
    });

    it('keeps the last count when a read fails, and plays nothing', async () => {
      create();
      await baseline([row('a')]);

      await changed();
      http.expectOne(READ).flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
      await vi.advanceTimersByTimeAsync(0);

      expect(service.count()).toBe(1);
      expect(sounds()).toEqual([]);
    });
  });

  describe('the workspace', () => {
    it('starts over for another workspace, with no count carried across', async () => {
      create();
      await baseline([row('a')]);

      tenant.set(null);
      TestBed.tick();
      await vi.advanceTimersByTimeAsync(0);

      expect(service.count()).toBeNull();
    });
  });

  it('can be asked for the count from inside an effect, as the screens do', async () => {
    create({ audible: false });

    TestBed.runInInjectionContext(() => {
      effect((onCleanup) => onCleanup(service.showCount()));
    });
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(0);

    await answer([row('a')]);
    expect(service.count()).toBe(1);
  });

  it('counts how many places show the count, so one leaving does not stop the rest', async () => {
    create({ audible: false });
    const first = service.showCount();
    const second = service.showCount();

    await start();
    await answer([row('a')]);

    first();
    TestBed.tick();

    await changed();
    await answer([row('a'), row('b')], 2);

    expect(service.count()).toBe(2);
    second();
  });
});
