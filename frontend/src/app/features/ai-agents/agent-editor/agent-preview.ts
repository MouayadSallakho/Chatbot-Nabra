import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { Icon } from '@app/shared/icon/icon';
import type { WebChatWidget } from '@app/features/channels/website-chat-api';
import { readableForeground } from '@app/features/embed/accent-color';
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
 *
 * <strong>It looks like the chat customers see</strong> (Phase 36.8): the
 * widget's header with the agent's name and the honesty line, the AI's
 * bubbles in the accent's tint, the customer's neutral, the composer at the
 * bottom. The accent and greeting are the workspace's real widget settings
 * when a website chat uses this agent; otherwise the widget's default colour,
 * and the panel says which. The words a customer would see are in the reader's
 * language here; the widget itself follows the visitor's.
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

  /** The agent's saved name: the widget's title. */
  readonly agentName = input.required<string>();

  /** The website widget's appearance for this agent, or null for the default look. */
  readonly widget = input<WebChatWidget | null>(null);

  /** Whether the widget lookup has finished (so the note does not flicker). */
  readonly widgetKnown = input(false);

  /** Whether the editor holds unsaved edits, so the panel can say the preview ignores them. */
  readonly unsavedChanges = input(false);

  /** The widget's own default accent, used until (or unless) the workspace's is known. */
  private static readonly DEFAULT_ACCENT = '#2563eb'; // token-lint-ignore: the widget's own default (embed-chat.scss)

  protected readonly accent = computed(
    () => this.widget()?.accentColor ?? AgentPreview.DEFAULT_ACCENT,
  );

  protected readonly onAccent = computed(() => readableForeground(this.accent()));

  protected readonly greeting = computed(() => this.widget()?.greeting ?? null);

  protected readonly limits = AGENT_LIMITS;

  protected readonly frameLabel = $localize`:@@agents.preview.frameLabel:Chat preview, as customers see it`;
  protected readonly transcriptLabel = $localize`:@@agents.preview.transcriptLabel:Preview conversation`;

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

  /**
   * Enter sends and Shift+Enter starts a new line, as in the widget. A key
   * held for an IME composition is left alone.
   */
  protected onEnter(event: Event): void {
    const key = event as KeyboardEvent;

    if (key.shiftKey || key.isComposing) {
      return;
    }

    key.preventDefault();
    void this.send();
  }

  /** Clears this tab's transcript. Nothing server-side is affected. */
  protected clear(): void {
    this.exchanges.set([]);
    this.failure.set(null);
  }
}
