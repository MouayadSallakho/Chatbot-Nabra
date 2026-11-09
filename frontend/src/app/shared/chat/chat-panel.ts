import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';
import type { ApiError, ApiErrorKind } from '@app/core/api/api-error';
import type { ChatHandling, ChatMessage } from '@app/features/chat/hosted-chat-api';
import { Icon } from '@app/shared/icon/icon';
import { ChatTranscript } from './chat-transcript';

/** What the panel is showing. Only `ready` has a transcript and a composer. */
export type ChatPanelState = 'opening' | 'unavailable' | 'failed' | 'ready';

let nextPanel = 0;

/**
 * The chat a visitor talks in (Phase 36.9): header, transcript, notices and
 * composer, as one component for the website widget, the hosted chat page and
 * the widget preview in the website chat settings.
 *
 * <strong>Presentational only.</strong> It holds no credential, calls no API
 * and keeps no conversation; the page around it does all of that and tells it
 * what to show. That is what lets the settings screen draw the real widget with
 * nothing behind it.
 *
 * <strong>Nothing here submits a form.</strong> The widget's frame is
 * sandboxed without `allow-forms`, so Send is an ordinary button and Enter is
 * handled as a keystroke (Shift+Enter adds a line; a key held for an IME
 * composition is left alone). The same code runs on the hosted page, which has
 * no sandbox, so the two cannot behave differently.
 *
 * <strong>Two looks, one structure.</strong> In the widget (`variant="widget"`)
 * the header and Send take the workspace's accent from `--chat-accent` and
 * `--chat-on-accent`, set from `accent-color.ts`, and the AI's replies its
 * tint. The hosted page wears the same accent when the workspace has a usable
 * one (`accented`); without one it is Nabra's own: an ink Send and the AI voice's
 * tint.
 */
@Component({
  selector: 'app-chat-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatTranscript, Icon],
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.scss',
  host: {
    class: 'chat-panel',
    '[class.is-widget]': 'variant() === "widget"',
    '[class.is-page]': 'variant() === "page"',
    '[class.has-accent]': 'accented()',
    '[class.is-preview]': 'preview()',
  },
})
export class ChatPanel {
  private readonly injector = inject(Injector);

  readonly variant = input<'widget' | 'page'>('widget');
  readonly state = input<ChatPanelState>('ready');

  /** The page's only `h1` in the widget and on the hosted page; a paragraph in a preview. */
  readonly titleIsHeading = input(true);

  readonly agentName = input('');

  /** Whose chat this is. Heads the panel when the page knows it. */
  readonly workspaceName = input('');

  /** On the hosted page: `--chat-accent` and `--chat-on-accent` hold a checked colour. */
  readonly accented = input(false);

  readonly greeting = input<string | null>(null);
  readonly messages = input<readonly ChatMessage[]>([]);
  readonly handling = input<ChatHandling>('Ai');
  readonly sending = input(false);
  readonly failure = input<ApiError | null>(null);

  /**
   * Failures said as information rather than as an error. Empty for visitors,
   * for whom any missing answer is a failure; the agent preview in the editor
   * names the unconfigured provider here, because to an administrator that is
   * a fact about the deployment.
   */
  readonly infoKinds = input<readonly ApiErrorKind[]>([]);

  /** Why the chat could not be opened, on the hosted page. */
  readonly openFailure = input<string | null>(null);

  readonly canSend = input(false);
  readonly hasConversation = input(false);
  readonly closable = input(false);
  readonly maxLength = input(4_000);

  /** Drawn for a settings screen: the composer is shown, and does nothing. */
  readonly preview = input(false);

  /** What the visitor is typing. The page clears it after a send succeeds. */
  readonly draft = model('');

  readonly send = output<void>();
  readonly newChat = output<void>();
  readonly closed = output<void>();
  readonly retry = output<void>();

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly composer = viewChild<ElementRef<HTMLTextAreaElement>>('composer');

  protected readonly ids = {
    title: `chat-title-${++nextPanel}`,
    input: `chat-input-${nextPanel}`,
    keys: `chat-keys-${nextPanel}`,
  };

  /** The alert's tone: a wait is a warning, a fact is information, the rest is an error. */
  protected alertTone(error: ApiError): 'warning' | 'info' | 'danger' {
    if (error.kind === 'rateLimited') {
      return 'warning';
    }

    return this.infoKinds().includes(error.kind) ? 'info' : 'danger';
  }

  protected readonly closeLabel = $localize`:@@visitor.close:Close chat`;
  private readonly defaultTitle = $localize`:@@visitor.defaultTitle:Chat`;

  /** The workspace's name; the agent's where a page has none to give. */
  protected readonly title = computed(
    () => this.workspaceName() || this.agentName() || this.defaultTitle,
  );

  constructor() {
    // Keep the newest message in view as the transcript grows, or while the AI
    // is typing. The visitor reads from the bottom of a chat.
    effect(() => {
      this.messages();
      this.sending();
      this.handling();
      this.failure();

      afterNextRender(
        () => {
          const element = this.scroller()?.nativeElement;

          if (element) {
            element.scrollTop = element.scrollHeight;
          }
        },
        { injector: this.injector },
      );
    });
  }

  /** Puts the cursor in the message box: on opening, and after starting a new chat. */
  focusComposer(): void {
    this.composer()?.nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  /** Enter sends; Shift+Enter starts a new line; an IME composition is left alone. */
  protected onEnter(event: Event): void {
    const key = event as KeyboardEvent;

    if (key.shiftKey || key.isComposing) {
      return;
    }

    key.preventDefault();
    this.submit();
  }

  protected submit(): void {
    if (this.preview() || !this.canSend()) {
      return;
    }

    if (this.draft().trim().length === 0) {
      this.focusComposer();

      return;
    }

    this.send.emit();
  }
}
