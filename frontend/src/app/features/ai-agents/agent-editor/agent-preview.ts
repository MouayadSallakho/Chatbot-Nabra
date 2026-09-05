import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Icon } from '@app/shared/icon/icon';
import { AGENT_LIMITS } from '../ai-agent-limits';
import { AiAgentApi } from '../ai-agent-api';

/** One exchange in the current preview session. */
interface Exchange {
  readonly message: string;
  readonly reply: string;
}

/**
 * A test bench for the agent, inside the editor.
 *
 * <strong>Single-turn, because the endpoint is stateless.</strong> Each request
 * carries one message and the agent's instructions; the backend stores nothing,
 * remembers nothing and chains nothing. The transcript below the box is this
 * browser tab's own record of what was tried -- it is never sent back as
 * context, never persisted, and disappears on reload. Presenting it as a
 * conversation the agent remembers would be a lie about what the platform does,
 * so the panel says what it is.
 *
 * <strong>It sends no `If-Match` and receives no entity tag.</strong> Preview
 * changes nothing, so there is no version to advance -- and nothing here can
 * touch the version the editor is holding for its own saves.
 *
 * <strong>It works in every lifecycle state.</strong> Previewing is how somebody
 * decides whether a draft is ready, and how they check one they have taken out
 * of service to repair. Status governs whether an agent serves customers, not
 * whether its owner may test it.
 */
@Component({
  selector: 'app-agent-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Icon],
  templateUrl: './agent-preview.html',
  styleUrl: './agent-preview.scss',
})
export class AgentPreview {
  private readonly api = inject(AiAgentApi);

  readonly tenantId = input.required<string>();
  readonly aiAgentId = input.required<string>();

  /** Whether the agent has unsaved instruction edits, so the panel can say so. */
  readonly unsavedInstructions = input(false);

  protected readonly limits = AGENT_LIMITS;

  /**
   * One control, but inside a group.
   *
   * The group is what supplies `ngSubmit` on the surrounding `<form>`. A bare
   * `[formControl]` does not: the event would be bound as a DOM listener for a
   * custom event that never fires, the button would appear to do nothing, and
   * the browser would perform a native form submission instead -- reloading the
   * page. Found by a test that clicked Send and saw no request.
   */
  protected readonly form = new FormGroup({
    message: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(AGENT_LIMITS.previewMessageMax)],
    }),
  });

  protected get message(): FormControl<string> {
    return this.form.controls.message;
  }

  protected readonly generating = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly exchanges = signal<readonly Exchange[]>([]);

  protected async send(): Promise<void> {
    const text = this.message.value.trim();

    if (text.length === 0 || this.generating()) {
      return;
    }

    this.generating.set(true);
    this.failure.set(null);

    try {
      const preview = await firstValueFrom(
        this.api.preview(this.tenantId(), this.aiAgentId(), text),
      );

      this.exchanges.update((current) => [...current, { message: text, reply: preview.reply }]);
      this.message.setValue('');
    } catch (error) {
      // Never retried automatically. A timeout in particular may mean the
      // provider is still working and still being billed, so a second request
      // doubles the cost of a situation already going badly. The reader decides.
      this.failure.set(toApiError(error));
    } finally {
      this.generating.set(false);
    }
  }

  /** Clears this tab's transcript. Nothing server-side is affected. */
  protected clear(): void {
    this.exchanges.set([]);
    this.failure.set(null);
  }
}
