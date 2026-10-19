import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AiAgentApi, AiAgentSummary } from '@app/features/ai-agents/ai-agent-api';
import { agentStatusWord } from '@app/features/ai-agents/agent-words';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import {
  CHANNEL_LIFECYCLE,
  LIFECYCLE_LABEL,
  channelStatusTone,
  channelStatusWord,
  whatsAppStatusSentence,
} from '../../channel-words';
import { WhatsAppApi, WhatsAppConnection } from '../../whatsapp-api';

/**
 * One WhatsApp connection.
 *
 * <strong>The access token is write-only, here as well as on the server.</strong>
 * There is no field that displays one, nothing is kept after a save, and the
 * only thing the screen says about it afterwards is whether one is configured.
 * A "reveal" control would need the server to return it, and it never will.
 *
 * <strong>A stale save keeps what somebody typed.</strong> Losing a 412 race
 * means the connection moved underneath the screen, not that the edits were
 * wrong — so the form keeps them and the reader is asked to re-read and decide.
 *
 * <strong>Where it stands, first</strong> (Phase 36.9): the lifecycle with the
 * current step marked, what it means for customers, whether a token is stored,
 * and the one action that changes it. Taking a number out of service is
 * confirmed exactly as before.
 */
@Component({
  selector: 'app-whatsapp-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, ConfirmDialog, Icon],
  templateUrl: './whatsapp-detail.html',
  styleUrl: './whatsapp-detail.scss',
})
export class WhatsAppDetail implements OnInit {
  private readonly disableDialog = viewChild.required<ConfirmDialog>('disableDialog');

  /** Bound from the route. */
  readonly connectionId = input.required<string>();

  private readonly api = inject(WhatsAppApi);
  private readonly agents = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly connection = signal<WhatsAppConnection | null>(null);
  protected readonly agentOptions = signal<readonly AiAgentSummary[]>([]);

  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly stale = signal(false);
  protected readonly saved = signal(false);

  protected readonly credentialSaving = signal(false);
  protected readonly credentialFailure = signal<ApiError | null>(null);
  protected readonly credentialStale = signal(false);
  protected readonly credentialSaved = signal(false);

  /** What the person has typed. Kept across a lost race. */
  protected readonly agentId = signal('');
  protected readonly wabaId = signal('');
  protected readonly phoneNumberId = signal('');
  protected readonly displayPhoneNumber = signal('');

  /**
   * The access token, while it is being typed and no longer.
   *
   * Cleared the moment a save succeeds, so it does not sit in memory — or in a
   * form a browser might offer to remember — after it has been stored.
   */
  protected readonly accessToken = signal('');

  /** The version the next conditional write must present. */
  private readonly version = signal<string | null>(null);

  protected readonly canView = computed(() => this.permissions.has('whatsapp.view'));
  protected readonly canManage = computed(() => this.permissions.has('whatsapp.manage'));

  protected readonly isActive = computed(() => this.connection()?.status === 'Active');

  protected readonly lifecycle = CHANNEL_LIFECYCLE;
  protected readonly lifecycleLabel = LIFECYCLE_LABEL;
  protected readonly statusWord = channelStatusWord;
  protected readonly statusTone = channelStatusTone;
  protected readonly agentStatusWord = agentStatusWord;

  protected readonly statusSentence = computed(() =>
    whatsAppStatusSentence(this.connection()?.status ?? 'Draft'),
  );

  protected readonly hasCredential = computed(() => this.connection()?.hasCredential === true);

  /** Whether the settings form holds enough to send. */
  protected readonly ready = computed(
    () => this.agentId() !== '' && this.digits(this.wabaId()) && this.digits(this.phoneNumberId()),
  );

  /**
   * Where Meta should be pointed.
   *
   * Built from the browser's own origin: the server does not reliably know what
   * hostname a browser reached it by, and this is the one place that does. It is
   * not a secret — it is a public route defended by a signature.
   */
  protected readonly webhookUrl = computed(() => `${window.location.origin}/api/whatsapp/webhook`);

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(this.connection() === null);
    this.failure.set(null);
    this.stale.set(false);
    this.credentialStale.set(false);

    try {
      const read = await firstValueFrom(this.api.get(tenantId, this.connectionId()));

      this.apply(read.value, read.etag);

      if (this.canManage() && this.agentOptions().length === 0) {
        const agents = await firstValueFrom(this.agents.list(tenantId, 1, 100));

        this.agentOptions.set(agents.items);
      }
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Saves the number and the agent, if the connection is still at this version. */
  protected async save(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const expected = this.version();

    if (!tenantId || !expected || !this.ready() || this.saving() || !this.canManage()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.stale.set(false);
    this.saved.set(false);

    try {
      const updated = await firstValueFrom(
        this.api.update(
          tenantId,
          this.connectionId(),
          {
            aiAgentId: this.agentId(),
            wabaId: this.wabaId().trim(),
            phoneNumberId: this.phoneNumberId().trim(),
            displayPhoneNumber: this.displayPhoneNumber().trim() || null,
          },
          expected,
        ),
      );

      this.apply(updated.value, updated.etag);
      this.saved.set(true);
    } catch (error) {
      const problem = toApiError(error);

      // <strong>The edits stay on screen.</strong> A stale write means somebody
      // else changed the connection first; what was typed here is still what
      // this person meant, and throwing it away would be the second thing to go
      // wrong.
      if (problem.kind === 'stale') {
        this.stale.set(true);
      } else {
        this.saveFailure.set(problem);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /** Stores a new access token. It is sent once and never read back. */
  protected async storeCredential(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const expected = this.version();
    const token = this.accessToken().trim();

    if (!tenantId || !expected || token === '' || this.credentialSaving() || !this.canManage()) {
      return;
    }

    this.credentialSaving.set(true);
    this.credentialFailure.set(null);
    this.credentialStale.set(false);
    this.credentialSaved.set(false);

    try {
      const updated = await firstValueFrom(
        this.api.replaceCredential(tenantId, this.connectionId(), token, expected),
      );

      this.apply(updated.value, updated.etag);

      // Cleared immediately. Nothing on this screen ever shows it again, and
      // there is no endpoint that could.
      this.accessToken.set('');
      this.credentialSaved.set(true);
    } catch (error) {
      const problem = toApiError(error);

      if (problem.kind === 'stale') {
        this.credentialStale.set(true);
      } else {
        this.credentialFailure.set(problem);
      }
    } finally {
      this.credentialSaving.set(false);
    }
  }

  /** Puts the connection into or out of service. */
  protected async setStatus(activate: boolean): Promise<void> {
    // Switching it off affects customers at once, so it is confirmed first;
    // switching it back on is not.
    if (!activate && !(await this.disableDialog().ask())) {
      return;
    }

    const tenantId = this.workspace.tenantId();
    const expected = this.version();

    if (!tenantId || !expected || this.saving() || !this.canManage()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.stale.set(false);
    this.saved.set(false);

    try {
      const updated = await firstValueFrom(
        this.api.setStatus(tenantId, this.connectionId(), activate, expected),
      );

      this.apply(updated.value, updated.etag);
    } catch (error) {
      const problem = toApiError(error);

      if (problem.kind === 'stale') {
        this.stale.set(true);
      } else {
        this.saveFailure.set(problem);
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected digits(value: string): boolean {
    const trimmed = value.trim();

    return trimmed.length > 0 && /^[0-9]+$/.test(trimmed);
  }

  /**
   * What is wrong with an identifier, said beside it -- otherwise Save settings
   * is simply disabled and the reader has to guess which field is the reason.
   */
  protected idProblem(value: string): string | null {
    if (value.trim().length === 0) {
      return $localize`:@@whatsapp.field.required:Required. Use the digits from your Meta app.`;
    }

    return this.digits(value)
      ? null
      : $localize`:@@whatsapp.field.digitsOnly:Use digits only: no spaces, letters or symbols.`;
  }

  /**
   * Takes what the server returned, and the version that came with it.
   *
   * The form fields are refilled from the saved connection rather than left as
   * typed, so what is on screen after a save is what is stored.
   */
  private apply(connection: WhatsAppConnection, etag: string): void {
    this.connection.set(connection);
    this.version.set(etag);

    this.agentId.set(connection.aiAgentId);
    this.wabaId.set(connection.wabaId);
    this.phoneNumberId.set(connection.phoneNumberId);
    this.displayPhoneNumber.set(connection.displayPhoneNumber ?? '');
  }
}
