import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { ChatMessage } from '@app/features/chat/hosted-chat-api';
import { Icon } from '@app/shared/icon/icon';
import { Orb } from '@app/shared/orb/orb';

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
 * <strong>Who spoke is said three ways, never by colour alone</strong>: the
 * three voices of Calm Glass (Phase 39.5a), as the inbox thread has them since
 * 39.4b, seen from the visitor's side. The visitor's own messages sit on the
 * end side in cream; the AI's replies on the start side beside the orb, in
 * emerald (in the widget, the workspace's checked accent as a tint) with the
 * agent's name; a person's replies on the start side in gold beside the person
 * glyph, with the word "Support". Never an employee's name: the server sends
 * none, and there is no field here that could hold one (owner decision Q5).
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
  imports: [Icon, Orb],
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
