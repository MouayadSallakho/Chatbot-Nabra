import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ToastService } from '@app/core/feedback/toast-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
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
 */
@Component({
  selector: 'app-agent-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Icon],
  templateUrl: './agent-create.html',
  styleUrl: './agent-create.scss',
})
export class AgentCreate {
  private readonly api = inject(AiAgentApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly limits = AGENT_LIMITS;

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

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    // Also the double-submit guard: without it a second click before the first
    // response creates a second agent.
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

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

      this.toasts.success(`${created.agent.name} created.`);

      // The created representation and its entity tag are handed to the editor
      // through navigation state, so it can open on canonical server state
      // without a second request. If anything about that handoff is missing the
      // editor simply reads the agent itself, so this is an optimisation rather
      // than something correctness depends on.
      await this.router.navigate(['/ai-agents', created.agent.id], {
        state: { [CREATED_AGENT_STATE]: created },
      });
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }
}
