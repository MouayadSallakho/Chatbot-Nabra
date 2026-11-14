import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { Chip } from '@app/shared/chip/chip';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { AGENT_LIMITS, CREATED_AGENT_STATE } from '../ai-agent-limits';
import { AiAgentApi } from '../ai-agent-api';

/**
 * Creating an AI agent.
 *
 * The reader supplies two things: a name and the instructions. Everything else
 * -- the identifier, the owning workspace, the status, both timestamps -- is the
 * backend's to decide, and none of them is expressible here. There is no model
 * or provider field either: which model answers is a deployment choice, not a
 * property of an agent.
 *
 * <strong>Client-side validation is for immediacy only.</strong> The two rules
 * mirrored here are the ones a reader hits while typing -- required, and a
 * length bound they can see counting down. Everything else stays with the
 * Domain, and a rejected value is reported in the backend's own words. Copying
 * the whole rule set into TypeScript would create a second authority that drifts
 * from the first.
 *
 * <strong>Guided, not a wizard</strong> (Phase 36.8): two numbered sections on
 * one page and, beside them, the real lifecycle a new agent follows (draft,
 * preview, knowledge, activate). Two fields do not need steps; they need the
 * reader to know nothing goes into service on Create.
 */
@Component({
  selector: 'app-agent-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState, Icon, LocalNumberPipe, Chip],
  templateUrl: './agent-create.html',
  styleUrl: './agent-create.scss',
})
export class AgentCreate {
  private readonly api = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /**
   * Whether the reader may create an agent.
   *
   * The list only offers this page to somebody who can; this is what answers
   * sensibly when they arrive by typing the URL instead.
   */
  protected readonly allowed = computed(() => this.permissions.has('ai-agents.create'));
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly limits = AGENT_LIMITS;

  protected readonly lifecycleLabel = $localize`:@@agents.lifecycle.label:Lifecycle`;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(AGENT_LIMITS.nameMax)],
    }),
    instructions: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(AGENT_LIMITS.instructionsMax)],
    }),
  });

  /** Live length of each field, for the counters. */
  protected readonly nameLength = signal(0);
  protected readonly instructionsLength = signal(0);

  protected readonly instructionsRemaining = computed(
    () => AGENT_LIMITS.instructionsMax - this.instructionsLength(),
  );

  constructor() {
    this.form.controls.name.valueChanges.subscribe((value) => this.nameLength.set(value.length));
    this.form.controls.instructions.valueChanges.subscribe((value) =>
      this.instructionsLength.set(value.length),
    );
  }

  protected showError(field: 'name' | 'instructions'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
  }

  /**
   * Takes the reader to the first field that needs attention, so the error they
   * are told about is where they are (the button is often far below it).
   */
  private focusFirstInvalid(): void {
    const id = this.form.controls.name.invalid ? 'agent-name' : 'agent-instructions';

    globalThis.document?.getElementById(id)?.focus();
  }

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    // Also the double-submit guard: without it a second click before the first
    // response creates a second agent.
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();

      return;
    }

    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const created = await firstValueFrom(
        this.api.create(tenantId, {
          name: this.form.controls.name.value.trim(),
          instructions: this.form.controls.instructions.value,
        }),
      );

      this.toasts.success(
        $localize`:@@agents.toast.created:${created.value.name}:name: created as a draft.`,
      );

      // The created representation and its entity tag are handed to the editor
      // through navigation state, so it can open on canonical server state
      // without a second request. If anything about that handoff is missing the
      // editor simply reads the agent itself, so this is an optimisation rather
      // than something correctness depends on.
      await this.router.navigate(['/ai-agents', created.value.id], {
        state: { [CREATED_AGENT_STATE]: created },
      });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }
}
