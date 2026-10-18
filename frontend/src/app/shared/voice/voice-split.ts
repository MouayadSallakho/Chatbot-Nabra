import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { VoiceChip } from './voice-chip';

/**
 * AI against people, as one bar: how many of something each voice has.
 *
 * Used for "open conversations right now" on the overview and for replies in
 * analytics. The counts are whatever the caller read from the API; this
 * component adds no number of its own, not even a percentage that the API did
 * not imply -- it only divides the two it was given.
 *
 * Said three ways, so no reader depends on the colours: the bar, a legend of
 * voice chips with their counts, and one plain sentence for assistive
 * technology (the bar and the legend are hidden from it, so it hears the
 * sentence once rather than the same numbers three times).
 *
 * The two parts are kept apart by a hairline of the surface: Lagoon and Ember
 * are close in lightness, and the seam is what separates them in greyscale.
 */
@Component({
  selector: 'app-voice-split',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [VoiceChip],
  template: `
    <div class="voice-split-bar" aria-hidden="true" [class.is-empty]="total() === 0">
      @if (ai() > 0) {
        <span class="voice-split-part voice-split-ai" [style.flex-grow]="ai()"></span>
      }
      @if (human() > 0) {
        <span class="voice-split-part voice-split-human" [style.flex-grow]="human()"></span>
      }
    </div>

    <ul class="voice-split-legend" aria-hidden="true">
      <li>
        <app-voice-chip voice="ai" size="sm" />
        <span class="voice-split-count">{{ ai() }}</span>
      </li>
      <li>
        <app-voice-chip voice="human" size="sm" />
        <span class="voice-split-count">{{ human() }}</span>
      </li>
    </ul>

    <p class="visually-hidden">{{ sentence() }}</p>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-2);
    }

    .voice-split-bar {
      display: flex;
      gap: 2px;
      block-size: 0.5rem;
      overflow: hidden;
      border-radius: var(--radius-pill);
      background-color: var(--surface-inset);
    }

    .voice-split-part {
      flex-basis: 0;
      min-inline-size: 2px;
    }

    .voice-split-ai {
      background-color: var(--chart-ai);
    }

    .voice-split-human {
      background-color: var(--chart-human);
    }

    .voice-split-legend {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-4);

      li {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
      }
    }

    .voice-split-count {
      font: var(--text-numeric);
      font-variant-numeric: tabular-nums;
      color: var(--text-primary);
    }

    @media (forced-colors: active) {
      .voice-split-bar {
        border: 1px solid CanvasText;
      }

      .voice-split-ai {
        background-color: CanvasText;
      }

      .voice-split-human {
        background-color: GrayText;
      }
    }
  `,
})
export class VoiceSplit {
  /** How many belong to the AI. */
  readonly ai = input.required<number>();

  /** How many belong to people on the team. */
  readonly human = input.required<number>();

  /**
   * What is being counted, in the plural, for the sentence: "open
   * conversations", "replies".
   */
  readonly noun = input('conversations');

  /** The same noun in the singular, for a total of exactly one. */
  readonly nounOne = input('conversation');

  protected readonly total = computed(() => this.ai() + this.human());

  protected readonly sentence = computed(() => {
    const ai = this.ai();
    const human = this.human();
    const total = ai + human;

    if (total === 0) {
      return `No ${this.noun()} yet, with the AI or with people.`;
    }
    const noun = total === 1 ? this.nounOne() : this.noun();
    return `${total} ${noun}: ${ai} with the AI, ${human} with people on the team.`;
  });
}
