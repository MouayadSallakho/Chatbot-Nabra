import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { ChatMessage } from '@app/features/chat/hosted-chat-api';
import { Icon } from '@app/shared/icon/icon';

/** One row of the transcript: a message, and whether the replying voice changed before it. */
interface TranscriptRow {
  readonly message: ChatMessage;
  readonly handoff: 'toPerson' | 'toAi' | null;
}

/**
 * What a visitor has said and been told, as the visitor sees it (Phase 36.9).
 *
 * Shared by the website widget, the hosted chat page and the widget preview in
 * the website chat settings, so the three cannot drift apart.
 *
 * <strong>Who spoke is said three ways, never by colour alone.</strong> The
 * visitor's own messages sit on the end side in a neutral bubble; the AI's
 * replies carry the workspace's accent as a tint and the agent's name; a
 * person's replies carry the person glyph and the word "Support". Never an
 * employee's name: the server sends none, and there is no field here that
 * could hold one.
 *
 * <strong>Every message is text.</strong> Interpolation escapes, nothing here
 * uses `innerHTML`, and every message is `dir="auto"`, so an Arabic question in
 * an English chat (or the reverse) keeps its own direction.
 *
 * <strong>A handoff line appears only where the replying voice really
 * changed</strong>, worked out from the authors of the messages the server
 * returned (MASTER signature 4). The conversation starts with the AI, so a
 * first reply from a person is a handoff too.
 */
@Component({
  selector: 'app-chat-transcript',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './chat-transcript.html',
  styleUrl: './chat-transcript.scss',
})
export class ChatTranscript {
  readonly messages = input<readonly ChatMessage[]>([]);
  readonly agentName = input('');
  readonly greeting = input<string | null>(null);

  /** The AI is working on a reply. Never shown while a person has the conversation. */
  readonly typing = input(false);

  /** Shown when there is nothing else: no greeting and no messages. */
  readonly showEmpty = input(true);

  protected readonly rows = computed<readonly TranscriptRow[]>(() => {
    let voice: 'AiAgent' | 'HumanAgent' = 'AiAgent';

    return this.messages().map((message) => {
      if (message.sender === 'Customer' || message.sender === voice) {
        return { message, handoff: null };
      }

      voice = message.sender;

      return { message, handoff: voice === 'HumanAgent' ? 'toPerson' : 'toAi' };
    });
  });

  protected readonly label = $localize`:@@visitor.transcript.label:Conversation`;
}
