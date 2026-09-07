import { DatePipe, LowerCasePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { WebChatAdminApi, WebChatEndpoint, hostedChatUrl } from '../../website-chat-api';

/**
 * One website chat link: its address, and whether the public may use it.
 *
 * <strong>A refused save keeps what the reader saw.</strong> A `412` means
 * somebody else changed this endpoint since it was read. Nothing is retried
 * automatically — replaying a publish against state nobody has seen is exactly
 * the lost update the precondition exists to prevent, and here it could put a
 * withdrawn chat back on the internet.
 */
@Component({
  selector: 'app-website-chat-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, LowerCasePipe, RouterLink, PageHeader, EmptyState],
  templateUrl: './website-chat-detail.html',
  styleUrl: './website-chat-detail.scss',
})
export class WebsiteChatDetail implements OnInit {
  private readonly api = inject(WebChatAdminApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** From the route, through `withComponentInputBinding`. */
  readonly webChatEndpointId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly endpoint = signal<WebChatEndpoint | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly stale = signal(false);
  protected readonly copied = signal(false);

  /** The version the next conditional write must present. */
  private readonly version = signal<string | null>(null);

  protected readonly canView = computed(() => this.permissions.has('website-chat.view'));
  protected readonly canManage = computed(() => this.permissions.has('website-chat.manage'));

  protected readonly isActive = computed(() => this.endpoint()?.status === 'Active');

  /** The address a visitor opens. */
  protected readonly chatUrl = computed(() => {
    const current = this.endpoint();

    return current ? hostedChatUrl(current.publicKey) : '';
  });

  /**
   * Whether publishing this link would put a chat online that cannot answer.
   *
   * A warning rather than a block: the server allows it, because an agent's
   * status can change at any moment and a check here would be a snapshot. What
   * guarantees no disabled agent answers the public is the runtime, which asks
   * again on every turn.
   */
  protected readonly agentNotActive = computed(() => this.endpoint()?.aiAgentStatus !== 'Active');

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
    this.stale.set(false);

    try {
      const read = await firstValueFrom(this.api.get(tenantId, this.webChatEndpointId()));

      this.endpoint.set(read.value);
      this.version.set(read.etag);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async setStatus(activate: boolean): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const expected = this.version();

    if (!tenantId || !expected || this.saving() || !this.canManage()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.stale.set(false);

    try {
      const updated = await firstValueFrom(
        this.api.setStatus(tenantId, this.webChatEndpointId(), activate, expected),
      );

      this.endpoint.set(updated.value);
      this.version.set(updated.etag);
    } catch (error) {
      const failure = toApiError(error);

      // A stale write is not an error beside a button: the endpoint moved
      // underneath the whole screen, and only re-reading fixes it.
      if (failure.kind === 'stale') {
        this.stale.set(true);
      } else {
        this.saveFailure.set(failure);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Puts the chat link on the clipboard.
   *
   * The address is shown as text as well, because the clipboard can be refused
   * by permission or by an insecure context — somebody who cannot copy must
   * still be able to select it by hand.
   */
  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.chatUrl());

      this.copied.set(true);
    } catch {
      this.copied.set(false);
    }
  }
}
