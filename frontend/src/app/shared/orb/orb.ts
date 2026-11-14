import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The orb: the AI's presence (calm-glass.md §6). A radial emerald disc, 26px
 * beside the AI's bubbles in a thread, 64px above the widget's greeting.
 *
 * While the AI is answering it breathes, scale 1 -> 1.04 and back, slowly.
 * Under reduced motion `--orb-breath` is 1 and it stays still. It is always
 * decorative (`aria-hidden`): the state is said in words beside it ("The AI is
 * answering…"), never by the orb alone.
 */
@Component({
  selector: 'app-orb',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'orb',
    'aria-hidden': 'true',
    '[class.orb-lg]': "size() === 'lg'",
    '[class.is-thinking]': 'thinking()',
  },
  template: ``,
  styles: `
    :host {
      display: inline-block;
      flex: none;
      inline-size: var(--orb-size);
      block-size: var(--orb-size);
      border-radius: var(--radius-pill);
      background: var(--orb-gradient);
    }

    :host(.orb-lg) {
      inline-size: var(--orb-size-lg);
      block-size: var(--orb-size-lg);
    }

    :host(.is-thinking) {
      animation: orb-breathe 2.4s var(--ease-calm) infinite alternate; /* motion-audit-ignore: infinite-animation, only while the AI answers; still under reduced motion (--orb-breath: 1) */
    }

    @keyframes orb-breathe {
      to {
        transform: scale(var(--orb-breath));
      }
    }

    @media (forced-colors: active) {
      :host {
        border: 2px solid CanvasText;
      }
    }
  `,
})
export class Orb {
  /** `sm` (26px) in threads and toasts; `lg` (64px) above the widget's greeting. */
  readonly size = input<'sm' | 'lg'>('sm');

  /** Breathing: only while the AI is answering. */
  readonly thinking = input(false);
}
