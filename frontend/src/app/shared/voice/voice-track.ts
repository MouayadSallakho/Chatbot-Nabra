import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { MessageSender } from '@app/features/conversations/conversations-api';
import { VOICE_WORDS, replyRuns, repliesPhrase } from './voice';

/**
 * The real sequence of reply authors in one conversation, as a 4px track:
 * Lagoon where the AI answered, Ember where a person did, in order.
 *
 * NEVER INVENTED. The track is derived from the messages it is given and from
 * nothing else -- no estimate, no "expected" handoff, no padding to a nicer
 * shape. When nobody has replied yet it renders nothing at all. A transcript is
 * paged, so a caller that holds only part of it says so with `partial`, and
 * the sentence says the same.
 *
 * Read in the reading direction: the first reply sits on the inline-start
 * side, so in Arabic the track runs from the right with no extra rule.
 */
@Component({
  selector: 'app-voice-track',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.is-empty]': 'runs().length === 0' },
  template: `
    @if (runs().length > 0) {
      <div class="voice-track" aria-hidden="true">
        @for (run of runs(); track $index) {
          <span
            class="voice-track-run"
            [class.voice-track-ai]="run.voice === 'ai'"
            [class.voice-track-human]="run.voice === 'human'"
            [style.flex-grow]="run.replies"
          ></span>
        }
      </div>
      <p class="visually-hidden">{{ sentence() }}</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    :host(.is-empty) {
      display: none;
    }

    .voice-track {
      display: flex;
      gap: 2px;
      block-size: var(--voice-track-height);
      overflow: hidden;
      border-radius: var(--radius-pill);
    }

    .voice-track-run {
      flex-basis: 0;
      min-inline-size: 4px;
    }

    .voice-track-ai {
      background-color: var(--voice-ai-fg);
    }

    .voice-track-human {
      background-color: var(--voice-human-fg);
    }

    @media (forced-colors: active) {
      .voice-track-ai {
        background-color: CanvasText;
      }

      .voice-track-human {
        background-color: GrayText;
      }
    }
  `,
})
export class VoiceTrack {
  /** The conversation's messages, oldest first, as the API returned them. */
  readonly messages = input.required<readonly { readonly sender: MessageSender }[]>();

  /** True when only some of the transcript is loaded. */
  readonly partial = input(false);

  protected readonly runs = computed(() => replyRuns(this.messages()));

  protected readonly sentence = computed(() => {
    const parts = this.runs().map(
      (run) => `${VOICE_WORDS[run.voice]}, ${repliesPhrase(run.replies)}`,
    );
    const scope = this.partial() ? 'Replies in the messages loaded so far' : 'Replies so far';
    return `${scope}: ${parts.join(', then ')}.`;
  });
}
