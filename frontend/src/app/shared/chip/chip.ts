import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Which word a chip carries, and so its hue (calm-glass.md §6, owner
 * decision Q4):
 *
 * - `need`, `team`, `waiting`: gold (needs a person, handled by, waiting for
 *   the team);
 * - `ai`: emerald (the AI is answering);
 * - `failed`: coral (the AI could not reply);
 * - `neutral`: a word with no voice and no status;
 * - `draft`: the neutral chip with a hollow ring in place of the dot, for
 *   something not yet in service (an AI agent's Draft, Phase 39.5a), so Draft
 *   and Disabled differ by marker as well as by word;
 * - `success`, `warning`, `danger`, `info`: status words that are not voices
 *   (quotas, billing, indexing), each with its own glyph in place of the dot.
 */
export type ChipTone =
  | 'need'
  | 'team'
  | 'waiting'
  | 'ai'
  | 'failed'
  | 'neutral'
  | 'draft'
  | 'success'
  | 'warning'
  | 'danger'
  | 'info';

/**
 * A chip: a 24px pill with a dot (or a status glyph) and a word.
 *
 * The word is projected and is the whole meaning: colour never says it alone.
 * The dot is drawn by the stylesheet (`.chip::before`), so it is never read
 * out. In the inbox and the transcript only the voice tones appear, so a
 * status chip never sits beside a voice chip of the same hue (Q4).
 *
 * ```html
 * <app-chip tone="need">Needs a person</app-chip>
 * ```
 */
@Component({
  selector: 'app-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class]': "'chip chip-' + tone() + (size() === 'sm' ? ' chip-sm' : '')",
    '[attr.data-tone]': 'tone()',
  },
  template: `<ng-content />`,
})
export class Chip {
  readonly tone = input<ChipTone>('neutral');

  /** `md` in headers and panels; `sm` (22px) inside dense rows. */
  readonly size = input<'sm' | 'md'>('md');
}
