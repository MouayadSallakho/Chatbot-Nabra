// Type-only, and the one place shared/ looks into a feature: the sender values
// are the API's contract, and a copy here could drift from it silently.
import type { MessageSender } from '@app/features/conversations/conversations-api';

/**
 * The two voices of every conversation: the AI, and a person on the team.
 *
 * The whole "Two Voices" direction rests on this one fact (docs/design/
 * MASTER.md). Lagoon means the AI and Ember means a person, and the two colours
 * mean nothing else -- never decoration, never status. Customers are not a
 * voice: they are the other side of the duet, drawn in neutral.
 */
export type Voice = 'ai' | 'human';

/** The word each voice is always shown with, so colour is never the only signal. */
export const VOICE_WORDS: Readonly<Record<Voice, string>> = { ai: 'AI', human: 'Person' };

/**
 * Which voice wrote a message, or null when nobody on the business side did.
 *
 * Read from the sender the platform recorded, never inferred from the text or
 * the timing: a customer message and a system notice belong to no voice.
 */
export function voiceOf(sender: MessageSender): Voice | null {
  switch (sender) {
    case 'AiAgent':
      return 'ai';
    case 'HumanAgent':
      return 'human';
    default:
      return null;
  }
}

/** A run of consecutive replies by the same voice. */
export interface VoiceRun {
  readonly voice: Voice;
  readonly replies: number;
}

/**
 * The real sequence of reply authors, folded into runs.
 *
 * Only replies count: customer messages and system notices are skipped, so a
 * customer writing between two AI answers does not split the AI's run. The
 * result is exactly what the given messages say, in their order, and nothing
 * more: an empty array when nobody has replied yet.
 */
export function replyRuns(messages: readonly { readonly sender: MessageSender }[]): VoiceRun[] {
  const runs: VoiceRun[] = [];

  for (const message of messages) {
    const voice = voiceOf(message.sender);
    if (voice === null) {
      continue;
    }

    const last = runs.at(-1);
    if (last?.voice === voice) {
      runs[runs.length - 1] = { voice, replies: last.replies + 1 };
    } else {
      runs.push({ voice, replies: 1 });
    }
  }

  return runs;
}

/** "1 reply", "3 replies". */
export function repliesPhrase(count: number): string {
  return `${count} ${count === 1 ? 'reply' : 'replies'}`;
}
