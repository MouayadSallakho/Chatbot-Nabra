import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { toApiError } from '@app/core/api/api-error';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  DeliveryScenario,
  MessengerSimulatorApi,
  SimulatorAgent,
  SimulatorMailbox,
} from '../messenger-simulator/messenger-api';
import {
  InstagramSimulatorApi,
  handoverNotice,
  type HandoverNotice,
  type HandoverOutcome,
} from '../instagram-simulator/instagram-api';

@Component({
  selector: 'app-social-simulator',
  imports: [PageHeader, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './social-simulator.html',
  styleUrl: './social-simulator.scss',
})
export class SocialSimulatorPage {
  protected readonly instagram = inject(ActivatedRoute).snapshot.data['channel'] === 'Instagram';
  protected readonly channelIcon = this.instagram ? 'instagram' : 'messenger';
  protected readonly heading = this.instagram
    ? $localize`:@@ig.title:Instagram Direct simulator`
    : $localize`:@@ms.title:Messenger simulator`;
  private readonly instagramApi = inject(InstagramSimulatorApi);
  private readonly api = this.instagram ? this.instagramApi : inject(MessengerSimulatorApi);
  private readonly workspace = inject(WorkspaceContextService);
  protected readonly format = inject(LocaleFormat);
  protected readonly agents = signal<readonly SimulatorAgent[] | null>(null);
  protected readonly agent = signal('');
  protected readonly mailbox = signal<SimulatorMailbox | null>(null);
  protected readonly scenario = signal<DeliveryScenario>('Accept');
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly issue = signal('');
  protected readonly readIssue = signal('');
  protected readonly recoveryNotice = signal('');
  protected readonly takeoverNotice = signal<HandoverNotice | null>(null);
  protected readonly handoverOutcome = signal<HandoverOutcome | null>(null);
  protected readonly outcomeText = {
    Applied: $localize`:@@ig.noticeApplied:Ownership notification applied.`,
    Duplicate: $localize`:@@ig.noticeDuplicate:Duplicate notification ignored. Control is unchanged.`,
    Stale: $localize`:@@ig.noticeStale:Older notification ignored. The latest owner keeps control.`,
    Conflict: $localize`:@@ig.noticeConflict:Conflicting notifications have the same time. Replies remain paused.`,
  };
  protected readonly loadingAgents = signal(false);
  protected readonly agentName = computed(
    () => this.agents()?.find((item) => item.id === this.agent())?.name,
  );
  protected readonly voices = {
    Customer: $localize`:@@ms.customer:Test customer`,
    AiAgent: $localize`:@@ms.ai:AI agent`,
    HumanAgent: $localize`:@@ms.team:Support team`,
  };
  private readonly history = viewChild<ElementRef<HTMLElement>>('history');
  private readonly drafts = new Map<string, { text: string; eventId: string }>();
  private eventId = '';
  private generation = 0;
  private latestRead = 0;
  private destroyed = false;
  private reading = false;

  constructor() {
    effect(() => {
      const tenant = this.workspace.tenantId();
      untracked(() => {
        this.drafts.clear();
        this.reset();
        this.agents.set(null);
        if (tenant) void this.loadAgents();
      });
    });
    const interval = setInterval(() => {
      if (
        document.visibilityState === 'visible' &&
        this.mailbox()?.conversationId &&
        !this.busy() &&
        !this.reading
      )
        void this.refresh();
    }, 4000);
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      clearInterval(interval);
    });
  }

  private reset(): void {
    this.generation++;
    this.takeoverNotice.set(null);
    this.handoverOutcome.set(null);
    this.latestRead++;
    this.reading = false;
    this.recoveryNotice.set('');
    this.agent.set('');
    this.scenario.set('Accept');
    this.mailbox.set(null);
    this.edit('');
    this.busy.set(false);
    this.issue.set('');
    this.readIssue.set('');
  }

  protected async loadAgents(): Promise<void> {
    const tenant = this.workspace.tenantId();
    if (!tenant) return;
    const generation = this.generation;
    this.loadingAgents.set(true);
    this.issue.set('');
    try {
      const agents = await firstValueFrom(this.api.agents(tenant));
      if (!this.current(generation)) return;
      this.agents.set(agents);
      if (agents.length) this.select(agents[0].id);
    } catch (error) {
      if (this.current(generation)) this.issue.set(toApiError(error).message);
    } finally {
      if (!this.destroyed && this.workspace.tenantId() === tenant) this.loadingAgents.set(false);
    }
  }

  protected select(id: string): void {
    const previous = this.agent();
    if (previous && this.draft())
      this.drafts.set(previous, { text: this.draft(), eventId: this.eventId });
    else this.drafts.delete(previous);
    this.reset();
    this.agent.set(id);
    const saved = this.drafts.get(id);
    if (saved) {
      this.draft.set(saved.text);
      this.eventId = saved.eventId;
    }
    void this.refresh();
  }

  protected edit(value: string): void {
    this.draft.set(value);
    this.eventId = '';
  }

  private current(generation: number): boolean {
    return !this.destroyed && this.generation === generation;
  }

  private show(box: SimulatorMailbox): void {
    const el = this.history()?.nativeElement;
    const atEnd = !el || el.scrollHeight - el.scrollTop - el.clientHeight < 48;
    const changed = box.messages.at(-1)?.id !== this.mailbox()?.messages.at(-1)?.id;
    this.mailbox.set(box);
    if (changed && atEnd)
      requestAnimationFrame(() => {
        const list = this.history()?.nativeElement;
        if (list) list.scrollTop = list.scrollHeight;
      });
  }

  protected async refresh(): Promise<void> {
    const tenant = this.workspace.tenantId(),
      agent = this.agent();
    if (!tenant || !agent || this.busy()) return;
    const generation = this.generation,
      revision = ++this.latestRead;
    this.reading = true;
    try {
      const box = await firstValueFrom(this.api.receive(tenant, agent, this.scenario()));
      if (this.current(generation) && revision === this.latestRead) {
        this.show(box);
        this.readIssue.set('');
      }
    } catch (error) {
      if (this.current(generation) && revision === this.latestRead)
        this.readIssue.set(toApiError(error).message);
    } finally {
      if (this.current(generation) && revision === this.latestRead) this.reading = false;
    }
  }

  protected setScenario(value: string): void {
    if (value === 'Accept' || value === 'Refuse' || value === 'LoseConfirmation')
      this.scenario.set(value);
  }

  protected async switchOwner(replay?: HandoverNotice): Promise<void> {
    const tenant = this.workspace.tenantId();
    const agent = this.agents()?.find((a) => a.id === this.agent());
    const mailbox = this.mailbox();
    if (!tenant || !agent || !mailbox?.ownership || this.busy()) return;
    const external = !mailbox.ownership.externalOwner;
    const notice =
      replay ??
      handoverNotice(
        agent,
        external,
        Math.max(Date.now(), (mailbox.lastHandoverTimestamp ?? 0) + 1),
      );
    const generation = this.generation;
    this.latestRead++;
    this.reading = false;
    this.busy.set(true);
    this.issue.set('');
    this.recoveryNotice.set('');
    try {
      const result = await firstValueFrom(this.instagramApi.notify(tenant, agent.id, notice));
      if (this.current(generation)) {
        this.show(result.mailbox);
        this.handoverOutcome.set(result.outcome);
        if (!replay && external) this.takeoverNotice.set(notice);
      }
    } catch (error) {
      if (this.current(generation)) this.issue.set(toApiError(error).message);
    } finally {
      if (this.current(generation)) this.busy.set(false);
    }
  }

  protected async recover(id: string): Promise<void> {
    const tenant = this.workspace.tenantId();
    if (!tenant || !this.agent() || this.busy()) return;
    const generation = this.generation;
    this.latestRead++;
    this.reading = false;
    this.busy.set(true);
    this.issue.set('');
    this.recoveryNotice.set('');
    try {
      const box = await firstValueFrom(this.api.recover(tenant, this.agent(), id));
      if (this.current(generation)) {
        this.show(box);
        this.recoveryNotice.set(
          box.deliveryIssues.some((item) => item.id === id)
            ? $localize`:@@ms.noReceipt:There is no receipt yet. The reply remains on hold.`
            : this.instagram
              ? $localize`:@@ig.recovered:The Instagram test account confirmed delivery.`
              : $localize`:@@ms.recovered:The simulated Page confirmed delivery.`,
        );
      }
    } catch (error) {
      if (this.current(generation)) this.issue.set(toApiError(error).message);
    } finally {
      if (this.current(generation)) this.busy.set(false);
    }
  }

  protected async send(): Promise<void> {
    const tenant = this.workspace.tenantId(),
      agent = this.agents()?.find((item) => item.id === this.agent()),
      text = this.draft().trim();
    if (!tenant || !agent || !text || text.length > 2000 || this.busy()) return;
    const generation = this.generation;
    this.latestRead++;
    this.reading = false;
    this.busy.set(true);
    this.recoveryNotice.set('');
    this.issue.set('');
    this.eventId ||= crypto.randomUUID();
    try {
      const box = await firstValueFrom(
        this.api.send(tenant, agent, this.eventId, text, this.scenario()),
      );
      if (this.current(generation)) {
        this.show(box);
        this.edit('');
        this.readIssue.set('');
      }
    } catch (error) {
      if (this.current(generation)) this.issue.set(toApiError(error).message);
    } finally {
      if (this.current(generation)) this.busy.set(false);
    }
  }
}
