import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { pluralMessage } from '@app/core/i18n/plural';
import { VoiceChip } from './voice-chip';

/** What a split bar counts. */
export type VoiceSplitSubject = 'conversations' | 'openConversations';

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
  imports: [VoiceChip, LocalNumberPipe],
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
        <span class="voice-split-count">{{ ai() | localNumber }}</span>
      </li>
      <li>
        <app-voice-chip voice="human" size="sm" />
        <span class="voice-split-count">{{ human() | localNumber }}</span>
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
      gap: var(--gap-hairline);
      block-size: 0.5rem;
      overflow: hidden;
      border-radius: var(--radius-pill);
      background-color: var(--color-panel-soft);
    }

    .voice-split-part {
      flex-basis: 0;
      min-inline-size: 2px;
    }

    .voice-split-ai {
      background-color: var(--chart-ai);
    }

    .voice-split-human {
      background-color: var(--chart-team);
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
      color: var(--color-text);
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
   * What is being counted, which chooses the screen-reader sentence. Whole
   * sentences per subject, because in Arabic the noun changes with the count.
   */
  readonly subject = input<VoiceSplitSubject>('conversations');

  private readonly format = inject(LocaleFormat);

  protected readonly total = computed(() => this.ai() + this.human());

  protected readonly sentence = computed(() => {
    const ai = this.ai();
    const human = this.human();
    const total = ai + human;

    const n = this.format.number(total);
    const a = this.format.number(ai);
    const h = this.format.number(human);
    const open = this.subject() === 'openConversations';

    // Nothing at all is its own sentence in both languages; English has no
    // plural category of its own for zero.
    if (total === 0) {
      return open
        ? $localize`:@@voice.split.open.zero:No open conversations yet, with the AI or with people.`
        : $localize`:@@voice.split.all.zero:No conversations yet, with the AI or with people.`;
    }

    return pluralMessage(
      this.format.language,
      total,
      open
        ? {
            zero: $localize`:@@voice.split.open.zero:No open conversations yet, with the AI or with people.`,
            one: $localize`:@@voice.split.open.one:${n}:total: open conversation: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            two: $localize`:@@voice.split.open.two:${n}:total: open conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            few: $localize`:@@voice.split.open.few:${n}:total: open conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            many: $localize`:@@voice.split.open.many:${n}:total: open conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            other: $localize`:@@voice.split.open.other:${n}:total: open conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
          }
        : {
            zero: $localize`:@@voice.split.all.zero:No conversations yet, with the AI or with people.`,
            one: $localize`:@@voice.split.all.one:${n}:total: conversation: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            two: $localize`:@@voice.split.all.two:${n}:total: conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            few: $localize`:@@voice.split.all.few:${n}:total: conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            many: $localize`:@@voice.split.all.many:${n}:total: conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
            other: $localize`:@@voice.split.all.other:${n}:total: conversations: ${a}:ai: with the AI, ${h}:human: with people on the team.`,
          },
    );
  });
}
