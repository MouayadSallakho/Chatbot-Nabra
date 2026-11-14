import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { VOICE_WORDS, Voice } from './voice';

/**
 * Who is speaking: "AI" in emerald or "Person" in gold (calm-glass.md §6).
 *
 * A chip: the voice's dot, then the word (`.chip` in the primitives). The word
 * says it, so colour never says it alone: in greyscale, or to somebody who
 * cannot tell the two apart, the word still does.
 *
 * THE "TAKE OVER" MOMENT. When the same chip changes voice -- a person takes a
 * conversation over, or hands it back -- its colours cross over in the dialog
 * duration. Under reduced motion the duration tokens collapse to nothing and
 * the change is instant; the word changes either way.
 *
 * Not a status badge: a voice is who replies, not whether something is fine.
 */
@Component({
  selector: 'app-voice-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'chip voice-chip',
    '[class.chip-ai]': "voice() === 'ai'",
    '[class.chip-team]': "voice() === 'human'",
    '[class.voice-chip-ai]': "voice() === 'ai'",
    '[class.voice-chip-human]': "voice() === 'human'",
    '[class.chip-sm]': "size() === 'sm'",
    '[class.voice-chip-sm]': "size() === 'sm'",
    '[attr.data-voice]': 'voice()',
  },
  template: `<span class="voice-chip-word">{{ word() }}</span>`,
  styles: `
    :host {
      flex: none;
      transition-property: background-color, color;
      transition-duration: var(--duration-dialog);
      transition-timing-function: var(--ease-calm);
    }

    :host::before {
      transition: background-color var(--duration-dialog) var(--ease-calm);
    }
  `,
})
export class VoiceChip {
  /** Which voice this chip names. */
  readonly voice = input.required<Voice>();

  /** `md` beside a heading or in a header; `sm` inside dense rows and legends. */
  readonly size = input<'sm' | 'md'>('md');

  /**
   * A more specific word where the screen knows one, such as a colleague's
   * name. The dot and colour still say which voice it is.
   */
  readonly label = input('');

  protected readonly word = computed(() => this.label() || VOICE_WORDS[this.voice()]);
}
