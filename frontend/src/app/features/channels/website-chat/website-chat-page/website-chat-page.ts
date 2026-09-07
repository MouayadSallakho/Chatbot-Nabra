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
import { WebChatAdminApi, WebChatEndpoint } from '../../website-chat-api';

/**
 * The workspace's website chat links.
 *
 * <strong>Creating one and publishing it are two steps.</strong> A link is
 * created switched off, so a mistyped agent selection is never live the moment
 * it is saved. Activation happens on the endpoint's own screen, behind a
 * conditional write.
 */
@Component({
  selector: 'app-website-chat-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState],
  templateUrl: './website-chat-page.html',
  styleUrl: './website-chat-page.scss',
})
export class WebsiteChatPage implements OnInit {
  private readonly api = inject(WebChatAdminApi);
  private readonly agents = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly endpoints = signal<readonly WebChatEndpoint[]>([]);
  protected readonly agentOptions = signal<readonly AiAgentSummary[]>([]);
  protected readonly creating = signal(false);
  protected readonly createFailure = signal<ApiError | null>(null);
  protected readonly selectedAgentId = signal<string>('');

  protected readonly canView = computed(() => this.permissions.has('website-chat.view'));
  protected readonly canManage = computed(() => this.permissions.has('website-chat.manage'));

  /**
   * The agents that could be given a chat link.
   *
   * Agents that already have one are left out, because a second link for the
   * same agent is refused by the server and offering it would be offering a
   * failure.
   */
  protected readonly available = computed(() => {
    const taken = new Set(this.endpoints().map((endpoint) => endpoint.aiAgentId));

    return this.agentOptions().filter((agent) => !taken.has(agent.id));
  });

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
      const endpoints = await firstValueFrom(this.api.list(tenantId));

      // Only asked for when it could be used. Somebody who cannot publish a link
      // has no use for the list of candidates, and requesting it would put a 403
      // in their console for no reason.
      const agents = this.canManage()
        ? await firstValueFrom(this.agents.list(tenantId, 1, 100))
        : null;

      this.endpoints.set(endpoints);
      this.agentOptions.set(agents?.items ?? []);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected choose(agentId: string): void {
    this.selectedAgentId.set(agentId);
    this.createFailure.set(null);
  }

  protected async create(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const agentId = this.selectedAgentId();

    if (!tenantId || !agentId || this.creating() || !this.canManage()) {
      return;
    }

    this.creating.set(true);
    this.createFailure.set(null);

    try {
      await firstValueFrom(this.api.create(tenantId, agentId));

      this.selectedAgentId.set('');

      await this.load();
    } catch (error) {
      this.createFailure.set(toApiError(error));
    } finally {
      this.creating.set(false);
    }
  }
}
