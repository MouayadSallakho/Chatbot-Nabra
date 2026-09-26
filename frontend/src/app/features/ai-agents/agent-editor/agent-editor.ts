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
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { HasUnsavedChanges, protectUnsavedChanges } from '@app/core/navigation/unsaved-changes';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ConfirmDialog } from '@app/core/feedback/confirm-dialog';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { AgentKnowledgeSection } from '@app/features/knowledge/agent-knowledge/agent-knowledge';
import { AgentPreview } from './agent-preview';
import { AGENT_LIMITS, CREATED_AGENT_STATE } from '../ai-agent-limits';
import { AiAgent, AiAgentApi } from '../ai-agent-api';
import { Versioned } from '@app/core/api/versioned';

/** Which save ran into a stale version. */
type ConflictSource = 'name' | 'instructions' | 'lifecycle';

/**
 * The agent editor: identity, instructions, lifecycle and a test bench.
 *
 * ENTITY TAG STATE IS THE HEART OF THIS SCREEN.
 *
 * The tag is read from the `ETag` header when the agent loads, kept in a signal,
 * sent as `If-Match` on every mutation, and **replaced with the new tag from
 * each successful response**. It is never shown, never edited, never parsed and
 * never constructed here -- it is a string the server produced, and the moment a
 * client starts deriving them the lost-update guarantee is gone.
 *
 * The tag reflects the version the *reader* saw, which is the whole point.
 * Somebody who opened this page ten minutes ago and presses Save is checked
 * against what they were looking at, not against whatever the server holds now.
 *
 * WHAT HAPPENS ON 412 IS DELIBERATE. The save is refused, the reader's text
 * stays exactly where it is, and nothing is retried, merged or overwritten. They
 * are told what happened and offered a reload -- which warns first if it would
 * discard their work. Silently replacing the editor with server state would
 * destroy the thing they were in the middle of writing, which is precisely the
 * loss the precondition exists to prevent.
 */
@Component({
  selector: 'app-agent-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    PageHeader,
    EmptyState,
    Icon,
    ConfirmDialog,
    AgentPreview,
    AgentKnowledgeSection,
  ],
  templateUrl: './agent-editor.html',
  styleUrl: './agent-editor.scss',
})
export class AgentEditor implements OnInit, HasUnsavedChanges {
  private readonly api = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly toasts = inject(ToastService);
  private readonly router = inject(Router);

  /** Bound from the route through `withComponentInputBinding`. */
  readonly aiAgentId = input.required<string>();

  protected readonly limits = AGENT_LIMITS;

  protected readonly loading = signal(true);
  protected readonly loadFailure = signal<ApiError | null>(null);

  /** The canonical agent, exactly as the server last described it. */
  protected readonly agent = signal<AiAgent | null>(null);

  /**
   * The version every mutation is checked against.
   *
   * Null means this screen has nothing safe to write with. Rather than sending
   * an unconditional mutation -- which the backend would refuse with 428 anyway,
   * and which would be a lost update waiting to happen if it ever did not -- the
   * save controls are blocked and a reload is offered.
   */
  protected readonly etag = signal<string | null>(null);

  protected readonly savingName = signal(false);
  protected readonly savingInstructions = signal(false);
  protected readonly changingLifecycle = signal(false);

  protected readonly conflict = signal<ConflictSource | null>(null);
  protected readonly lifecycleFailure = signal<ApiError | null>(null);
  protected readonly nameFailure = signal<ApiError | null>(null);
  protected readonly instructionsFailure = signal<ApiError | null>(null);

  protected readonly name = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(AGENT_LIMITS.nameMax)],
  });

  protected readonly instructions = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(AGENT_LIMITS.instructionsMax)],
  });

  /** Live values, so dirty state and counters are derived rather than tracked. */
  private readonly nameValue = signal('');
  private readonly instructionsValue = signal('');

  private readonly confirmDisable = viewChild<ConfirmDialog>('confirmDisable');

  /** Whether the field differs from what the server holds. */
  protected readonly nameChanged = computed(
    () => this.nameValue().trim() !== (this.agent()?.name ?? '') && this.nameValue().trim() !== '',
  );

  protected readonly instructionsChanged = computed(
    () => this.instructionsValue() !== (this.agent()?.instructions ?? ''),
  );

  /** Any unsaved edit at all, for the navigation warning. */
  protected readonly dirty = computed(() => this.nameChanged() || this.instructionsChanged());

  /** Whether leaving now would lose edits; asked by the route guard. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  protected readonly instructionsRemaining = computed(
    () => AGENT_LIMITS.instructionsMax - this.instructionsValue().length,
  );

  /** True while the screen has no usable version to write against. */
  /**
   * Whether the reader may change this agent's name or instructions.
   *
   * Without it the fields become read-only rather than disappearing: somebody
   * with `ai-agents.view` is entitled to read an agent's configuration, and
   * hiding the text they came to read would be worse than showing it plainly.
   */
  protected readonly canEdit = computed(() => this.permissions.has('ai-agents.edit'));

  /** Whether the reader may put this agent in or out of service. */
  protected readonly canChangeLifecycle = computed(() =>
    this.permissions.has('ai-agents.lifecycle'),
  );

  /** Whether to offer the preview panel at all. */
  protected readonly canPreview = computed(() => this.permissions.has('ai-agents.preview'));

  /**
   * Whether a conditional write is possible.
   *
   * Two independent reasons it might not be: there is no entity tag to present,
   * or the reader does not hold the editing permission. Both disable the same
   * controls, so they are combined -- but the template distinguishes them, since
   * "not yet loaded" and "not yours to change" need different words.
   */
  protected readonly cannotSave = computed(() => this.etag() === null || !this.canEdit());

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  /**
   * A just-created agent, handed over by the create page.
   *
   * Read in the constructor because that is the only moment it is available:
   * `currentNavigation()` is null once the navigation has finished.
   */
  private readonly handedOver = this.router.currentNavigation()?.extras.state?.[
    CREATED_AGENT_STATE
  ] as Versioned<AiAgent> | undefined;

  constructor() {
    // The tab being closed or reloaded is the other way unsaved edits are lost.
    protectUnsavedChanges(() => this.dirty());

    this.name.valueChanges.subscribe((value) => this.nameValue.set(value));
    this.instructions.valueChanges.subscribe((value) => this.instructionsValue.set(value));
  }

  /**
   * Loads the agent.
   *
   * In `ngOnInit` rather than the constructor because `aiAgentId` is a required
   * input, and Angular sets inputs after construction -- reading it a moment too
   * early throws rather than returning undefined.
   */
  ngOnInit(): void {
    if (this.handedOver) {
      // Already have the representation and its tag, so nothing is re-read.
      this.adopt(this.handedOver);
      this.loading.set(false);

      return;
    }

    void this.load();
  }

  /** Reads the agent and the version it is at. */
  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailure.set(null);

    try {
      this.adopt(await firstValueFrom(this.api.get(this.tenantId(), this.aiAgentId())));
    } catch (error) {
      this.loadFailure.set(toApiError(error));
      this.agent.set(null);
      this.etag.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  // --------------------------------------------------------------------- name

  protected async saveName(): Promise<void> {
    // No request when nothing changed. A "save" that rewrites the same value
    // would still move updatedAt and still advance the version, which would then
    // make somebody else's open editor stale for no reason at all.
    if (!this.nameChanged() || this.name.invalid || this.savingName() || this.cannotSave()) {
      return;
    }

    this.savingName.set(true);
    this.nameFailure.set(null);
    this.conflict.set(null);

    try {
      this.adopt(
        await firstValueFrom(
          this.api.rename(
            this.tenantId(),
            this.aiAgentId(),
            this.nameValue().trim(),
            this.etag() as string,
          ),
        ),
      );

      this.toasts.success('Name saved.');
    } catch (error) {
      this.handleMutationFailure(error, 'name');
    } finally {
      this.savingName.set(false);
    }
  }

  protected resetName(): void {
    this.name.setValue(this.agent()?.name ?? '');
    this.nameFailure.set(null);
  }

  // ------------------------------------------------------------- instructions

  protected async saveInstructions(): Promise<void> {
    if (
      !this.instructionsChanged() ||
      this.instructions.invalid ||
      this.savingInstructions() ||
      this.cannotSave()
    ) {
      return;
    }

    this.savingInstructions.set(true);
    this.instructionsFailure.set(null);
    this.conflict.set(null);

    try {
      this.adopt(
        await firstValueFrom(
          this.api.updateInstructions(
            this.tenantId(),
            this.aiAgentId(),
            this.instructionsValue(),
            this.etag() as string,
          ),
        ),
      );

      this.toasts.success('Instructions saved.');
    } catch (error) {
      this.handleMutationFailure(error, 'instructions');
    } finally {
      this.savingInstructions.set(false);
    }
  }

  protected resetInstructions(): void {
    this.instructions.setValue(this.agent()?.instructions ?? '');
    this.instructionsFailure.set(null);
  }

  // ---------------------------------------------------------------- lifecycle

  protected async activate(): Promise<void> {
    await this.changeLifecycle(
      () => this.api.activate(this.tenantId(), this.aiAgentId(), this.etag() as string),
      'Agent activated.',
    );
  }

  /** Asks first: taking an agent out of service affects live behaviour. */
  protected requestDisable(): void {
    this.confirmDisable()?.open();
  }

  protected async disable(): Promise<void> {
    await this.changeLifecycle(
      () => this.api.disable(this.tenantId(), this.aiAgentId(), this.etag() as string),
      'Agent disabled.',
    );
  }

  private async changeLifecycle(
    call: () => ReturnType<AiAgentApi['activate']>,
    success: string,
  ): Promise<void> {
    if (this.changingLifecycle() || this.cannotSave()) {
      return;
    }

    this.changingLifecycle.set(true);
    this.lifecycleFailure.set(null);
    this.conflict.set(null);

    try {
      this.adopt(await firstValueFrom(call()));
      this.toasts.success(success);
    } catch (error) {
      this.handleMutationFailure(error, 'lifecycle');
    } finally {
      this.changingLifecycle.set(false);
    }
  }

  // ----------------------------------------------------------------- conflict

  /**
   * Reloads the server's current state, after asking if that would cost work.
   *
   * The confirmation is the point: the reader's unsaved text is still on screen
   * after a 412, and replacing it is their decision to make, not this
   * component's.
   */
  protected async reloadLatest(): Promise<void> {
    if (this.dirty() && !this.confirmReplace()) {
      return;
    }

    this.conflict.set(null);
    this.nameFailure.set(null);
    this.instructionsFailure.set(null);
    this.lifecycleFailure.set(null);

    await this.load();
  }

  /**
   * Whether the reader accepts losing their edits.
   *
   * Deliberately overridable in tests -- the browser's own confirm is the
   * smallest correct thing here (it is modal, keyboard-accessible and cannot be
   * missed), but it cannot be driven from a unit test.
   */
  protected confirmReplace(): boolean {
    return globalThis.confirm(
      'Reloading replaces your unsaved changes with the latest saved version. Continue?',
    );
  }

  /**
   * Sorts a failed mutation into the three things it can mean.
   *
   * A stale version, a lifecycle refusal and everything else lead to different
   * repairs, so they are never collapsed into one "save failed" message.
   */
  private handleMutationFailure(failure: unknown, source: ConflictSource): void {
    const error = toApiError(failure);

    // 412 and 428 both mean "this screen's version is not usable". The tag is
    // dropped so no further mutation can be attempted with it, which is what
    // forces a deliberate reload rather than a retry loop.
    if (error.kind === 'stale' || error.kind === 'preconditionRequired') {
      this.conflict.set(source);
      this.etag.set(null);

      return;
    }

    if (source === 'name') {
      this.nameFailure.set(error);
    } else if (source === 'instructions') {
      this.instructionsFailure.set(error);
    } else {
      this.lifecycleFailure.set(error);
    }

    // A lifecycle refusal is about the agent's state, not about the request, so
    // the displayed status may be out of date. Re-reading costs one request and
    // stops the screen arguing with the server about what the agent is.
    if (error.kind === 'conflict') {
      void this.refreshQuietly();
    }
  }

  /** Re-reads the agent without disturbing what the reader is editing. */
  private async refreshQuietly(): Promise<void> {
    try {
      const current = await firstValueFrom(this.api.get(this.tenantId(), this.aiAgentId()));

      this.agent.set(current.value);
      this.etag.set(current.etag);

      // Only fields the reader is not editing are refreshed. Overwriting an
      // in-progress edit because a background read completed is exactly the
      // behaviour the conflict handling exists to avoid.
      if (!this.nameChanged()) {
        this.name.setValue(current.value.name, { emitEvent: false });
        this.nameValue.set(current.value.name);
      }

      if (!this.instructionsChanged()) {
        this.instructions.setValue(current.value.instructions, { emitEvent: false });
        this.instructionsValue.set(current.value.instructions);
      }
    } catch {
      // A failed background refresh is not worth interrupting the reader for.
      // The primary failure they were shown still stands.
    }
  }

  /**
   * Accepts a new version produced by something else that writes to this agent.
   *
   * Knowledge assignment lives in its own component but is written against this
   * agent's entity tag and advances it. Without this the editor would keep
   * holding a tag that is one version behind, and the reader's next save of the
   * name or the instructions would be refused for a change they made themselves.
   */
  protected adoptVersion(etag: string): void {
    this.etag.set(etag);
  }

  /** Takes a server representation as the new canonical state. */
  private adopt(versioned: Versioned<AiAgent>): void {
    this.agent.set(versioned.value);
    this.etag.set(versioned.etag);

    this.name.setValue(versioned.value.name, { emitEvent: false });
    this.nameValue.set(versioned.value.name);

    this.instructions.setValue(versioned.value.instructions, { emitEvent: false });
    this.instructionsValue.set(versioned.value.instructions);
  }
}
