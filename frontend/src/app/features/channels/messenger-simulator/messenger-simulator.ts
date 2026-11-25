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
import { RouterLink } from '@angular/router';
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
} from './messenger-api';

@Component({
  selector: 'app-messenger-simulator',
  imports: [PageHeader, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './messenger-simulator.html',
  styleUrl: './messenger-simulator.scss',
})
export class MessengerSimulatorPage {
  private readonly api = inject(MessengerSimulatorApi);
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
