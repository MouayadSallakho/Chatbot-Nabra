import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { APP_LANGUAGE } from '@app/core/i18n/language';
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
      gap: var(--gap-hairline);
      block-size: var(--voice-track-height);
      overflow: hidden;
      border-radius: var(--radius-pill);
    }

    .voice-track-run {
      flex-basis: 0;
      min-inline-size: 4px;
    }

    .voice-track-ai {
      background-color: var(--voice-ai-label);
    }

    .voice-track-human {
      background-color: var(--voice-team-label);
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

  private readonly language = inject(APP_LANGUAGE);

  protected readonly runs = computed(() => replyRuns(this.messages()));

  protected readonly sentence = computed(() => {
    const parts = this.runs().map((run) => {
      const voice = VOICE_WORDS[run.voice];
      const replies = repliesPhrase(run.replies, this.language);

      return $localize`:@@voice.track.run:${voice}:voice:, ${replies}:replies:`;
    });
    const runs = parts.join($localize`:@@voice.track.then:, then `);

    return this.partial()
      ? $localize`:@@voice.track.partial:Replies in the messages loaded so far: ${runs}:runs:.`
      : $localize`:@@voice.track.complete:Replies so far: ${runs}:runs:.`;
  });
}
