import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiError, ApiErrorKind, toApiError } from '@app/core/api/api-error';
import { Icon } from '@app/shared/icon/icon';
import { ChatPanel } from '@app/shared/chat/chat-panel';
import type { ChatMessage } from '@app/features/chat/hosted-chat-api';
import type { WebChatWidget } from '@app/features/channels/website-chat-api';
import { DEFAULT_ACCENT, checkAccent } from '@app/features/embed/accent-color';
import { AGENT_LIMITS } from '../ai-agent-limits';
import { AiAgentApi } from '../ai-agent-api';

/**
 * The panel's name. The editor's card for a reader without the preview
 * permission uses it too, so both say the same thing in one message.
 */
export const PREVIEW_HEADING = $localize`:@@agents.preview.heading:Customer view`;

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
 * <strong>It is the chat customers see</strong> (Phase 36.11): the shared
 * `ChatPanel` the website widget and the hosted page use, in its widget look,
 * so the three cannot drift apart. The accent and greeting are the workspace's
 * real widget settings when a website chat uses this agent; otherwise the
 * widget's default colour, and the panel says which. The accent is checked by
 * the widget's own rules (`accent-color.ts`) before anything is painted with
 * it. The words a customer would see are in the reader's language here; the
 * widget itself follows the visitor's.
 */
@Component({
  selector: 'app-agent-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatPanel, Icon],
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

  /** The accent as the widget would paint it: checked, with its label colour. */
  protected readonly accent = computed(() =>
    checkAccent(this.widget()?.accentColor ?? DEFAULT_ACCENT),
  );

  protected readonly greeting = computed(() => this.widget()?.greeting ?? null);

  protected readonly limits = AGENT_LIMITS;

  protected readonly heading = PREVIEW_HEADING;
  protected readonly frameLabel = $localize`:@@agents.preview.frameLabel:Chat preview, as customers see it`;

  /**
   * An unconfigured provider is a fact about this deployment, not something
   * the reader did wrong, so it is said as information. A visitor is told the
   * same thing as a failure, because to them the answer did not arrive.
   */
  protected readonly infoKinds: readonly ApiErrorKind[] = ['providerUnavailable'];

  /** What is in the box. Cleared once a reply arrives, kept after a failure. */
  protected readonly draft = signal('');

  protected readonly generating = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly exchanges = signal<readonly Exchange[]>([]);

  /** Send is offered for a message with words in it, one at a time. */
  protected readonly canSend = computed(() => !this.generating() && this.draft().trim().length > 0);

  /**
   * This tab's exchanges as the transcript draws them. The ids exist only for
   * the list; nothing here came from, or goes to, a stored conversation.
   */
  protected readonly messages = computed<readonly ChatMessage[]>(() =>
    this.exchanges().flatMap((exchange, index) => [
      { id: `preview-${index}-q`, sender: 'Customer', text: exchange.message, sentAt: '' },
      { id: `preview-${index}-a`, sender: 'AiAgent', text: exchange.reply, sentAt: '' },
    ]),
  );

  protected async send(): Promise<void> {
    const text = this.draft().trim();

    if (text.length === 0 || text.length > AGENT_LIMITS.previewMessageMax || this.generating()) {
      return;
    }

    this.generating.set(true);
    this.failure.set(null);

    try {
      const preview = await firstValueFrom(
        this.api.preview(this.tenantId(), this.aiAgentId(), text),
      );

      this.exchanges.update((current) => [...current, { message: text, reply: preview.reply }]);
      this.draft.set('');
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
