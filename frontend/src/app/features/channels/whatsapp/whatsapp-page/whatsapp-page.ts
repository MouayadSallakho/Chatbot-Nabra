import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AiAgentApi, AiAgentSummary } from '@app/features/ai-agents/ai-agent-api';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { WhatsAppApi, WhatsAppConnection } from '../../whatsapp-api';

/**
 * The workspace's WhatsApp numbers.
 *
 * <strong>Connecting a number and putting it into service are three
 * steps.</strong> A connection is created switched off, the access token is
 * entered on its own screen, and only then can it be activated. A number that
 * went live the moment it was typed would receive customer messages it had no
 * credential to answer.
 */
@Component({
  selector: 'app-whatsapp-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState],
  templateUrl: './whatsapp-page.html',
  styleUrl: './whatsapp-page.scss',
})
export class WhatsAppPage implements OnInit {
  private readonly api = inject(WhatsAppApi);
  private readonly agents = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly connections = signal<readonly WhatsAppConnection[]>([]);
  protected readonly agentOptions = signal<readonly AiAgentSummary[]>([]);
  protected readonly creating = signal(false);
  protected readonly createFailure = signal<ApiError | null>(null);

  protected readonly agentId = signal('');
  protected readonly wabaId = signal('');
  protected readonly phoneNumberId = signal('');
  protected readonly displayPhoneNumber = signal('');

  protected readonly canView = computed(() => this.permissions.has('whatsapp.view'));
  protected readonly canManage = computed(() => this.permissions.has('whatsapp.manage'));

  /** Whether the form holds enough to send. */
  protected readonly ready = computed(
    () => this.agentId() !== '' && this.digits(this.wabaId()) && this.digits(this.phoneNumberId()),
  );

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      const connections = await firstValueFrom(this.api.list(tenantId));

      // Only asked for when it could be used. Somebody who cannot connect a
      // number has no use for the list of agents, and requesting it would put a
      // 403 in their console for no reason.
      const agents = this.canManage()
        ? await firstValueFrom(this.agents.list(tenantId, 1, 100))
        : null;

      this.connections.set(connections);
      this.agentOptions.set(agents?.items ?? []);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async create(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.ready() || this.creating() || !this.canManage()) {
      return;
    }

    this.creating.set(true);
    this.createFailure.set(null);

    try {
      await firstValueFrom(
        this.api.create(tenantId, {
          aiAgentId: this.agentId(),
          wabaId: this.wabaId().trim(),
          phoneNumberId: this.phoneNumberId().trim(),
          displayPhoneNumber: this.displayPhoneNumber().trim() || null,
        }),
      );

      this.agentId.set('');
      this.wabaId.set('');
      this.phoneNumberId.set('');
      this.displayPhoneNumber.set('');

      await this.load();
    } catch (error) {
      this.createFailure.set(toApiError(error));
    } finally {
      this.creating.set(false);
    }
  }

  /**
   * Meta's identifiers are digits, and the server insists.
   *
   * Checked here so somebody pasting a phone number with spaces and a plus is
   * told before a round trip, not so the client enforces anything.
   */
  protected digits(value: string): boolean {
    const trimmed = value.trim();

    return trimmed.length > 0 && /^[0-9]+$/.test(trimmed);
  }
}
