import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { VOICE_WORDS, Voice } from './voice';

/**
 * Who is speaking: "AI" in Lagoon or "Person" in Ember.
 *
 * Always the glyph AND the word. The two voice colours sit close in lightness,
 * and Ember sits near the warning amber in hue, so colour is never left to say
 * it alone -- in greyscale, or to somebody who cannot tell the two apart, the
 * word still does.
 *
 * THE "TAKE OVER" MOMENT. When the same chip changes voice -- a person takes a
 * conversation over, or hands it back -- its colours sweep across in 320ms on
 * the emphasized curve. Under reduced motion the duration tokens collapse to
 * nothing and the change is instant; the word changes either way.
 *
 * Not a status badge: a voice is who replies, not whether something is fine.
 * Status badges keep their own glyphs and words.
 */
@Component({
  selector: 'app-voice-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  host: {
    class: 'voice-chip',
    '[class.voice-chip-ai]': "voice() === 'ai'",
    '[class.voice-chip-human]': "voice() === 'human'",
    '[class.voice-chip-sm]': "size() === 'sm'",
    '[attr.data-voice]': 'voice()',
  },
  template: `
    <app-icon class="voice-chip-glyph" [name]="glyph()" [strokeWidth]="1.8" />
    <span class="voice-chip-word">{{ word() }}</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      flex: none;
      block-size: 1.375rem;
      padding-inline: var(--space-2) 0.625rem;
      border: 1px solid transparent;
      border-radius: var(--radius-pill);
      font: 600 0.75rem / 1 var(--font-sans);
      white-space: nowrap;
      transition-property: background-color, border-color, color;
      transition-duration: var(--duration-lg);
      transition-timing-function: var(--ease-emphasized);
    }

    :host(.voice-chip-sm) {
      block-size: 1.125rem;
      padding-inline: 0.375rem var(--space-2);
      font-size: 0.6875rem;
    }

    :host(.voice-chip-ai) {
      border-color: var(--voice-ai-border);
      background-color: var(--voice-ai-tint);
      color: var(--voice-ai-strong);
    }

    :host(.voice-chip-human) {
      border-color: var(--voice-human-border);
      background-color: var(--voice-human-tint);
      color: var(--voice-human-strong);
    }

    @media (forced-colors: active) {
      :host {
        border-color: CanvasText;
      }
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
   * name. The glyph and colour still say which voice it is.
   */
  readonly label = input('');

  protected readonly glyph = computed(() => (this.voice() === 'ai' ? 'ai' : 'person'));

  protected readonly word = computed(() => this.label() || VOICE_WORDS[this.voice()]);
}
