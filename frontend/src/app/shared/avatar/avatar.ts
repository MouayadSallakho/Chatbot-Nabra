import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import type { IconName } from '@app/shared/icon/icon-paths';

/** The channel a person wrote on, as the API names it. */
type AvatarChannel = 'WebChat' | 'WhatsApp' | 'Instagram' | 'Messenger';

const BADGE_GLYPHS: Readonly<Record<AvatarChannel, IconName>> = {
  WebChat: 'website',
  WhatsApp: 'whatsapp',
  Instagram: 'instagram',
  Messenger: 'messenger',
};

/**
 * Up to two letters for a name, or the first two characters of a short code
 * ("7KQ2MX" -> "7K"): the first letter of the first and last words. An Arabic
 * name gives its first letter only. An empty name gives an empty string, and
 * the avatar then shows the plain disc.
 */
export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/u).filter(Boolean);

  if (words.length === 0) {
    return '';
  }

  const first = Array.from(words[0]);

  // Arabic letters join, so two initials would read as a fragment of a word:
  // one letter, as Arabic monograms are written.
  if (/^\p{Script=Arabic}/u.test(first[0])) {
    return first[0];
  }

  if (words.length === 1) {
    return first.slice(0, 2).join('').toLocaleUpperCase();
  }

  const last = Array.from(words[words.length - 1]);

  return (first[0] + last[0]).toLocaleUpperCase();
}

/**
 * A person's avatar (calm-glass.md §6): initials or a short code on cream, 40px
 * in rows, 34px for the member, 64px on the customer panel, with the channel's
 * badge at the end-bottom corner (WhatsApp green, the website emerald, each
 * with its glyph, so the badge is never colour alone).
 *
 * Decorative: the row or panel it sits in names the person and the channel in
 * words, so the avatar is hidden from assistive technology. The letters are
 * isolated (`dir="auto"`), so an Arabic name's initials and a Latin code both
 * read in their own direction.
 */
@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  host: {
    class: 'avatar',
    'aria-hidden': 'true',
    '[class.avatar-sm]': "size() === 'sm'",
    '[class.avatar-lg]': "size() === 'lg'",
  },
  template: `
    <span class="avatar-letters" dir="auto">{{ letters() }}</span>
    @if (badge(); as glyph) {
      <span class="avatar-badge" [attr.data-channel]="channel()">
        <app-icon [name]="glyph" [strokeWidth]="2.2" />
      </span>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-grid;
      place-items: center;
      flex: none;
      inline-size: var(--avatar-size);
      block-size: var(--avatar-size);
      border-radius: var(--radius-pill);
      background-color: var(--avatar-bg);
      color: var(--avatar-fg);
      font: 600 0.8125rem / 1 var(--font-sans);
      letter-spacing: 0;
    }

    :host(.avatar-sm) {
      inline-size: var(--avatar-size-sm);
      block-size: var(--avatar-size-sm);
      font-size: 0.75rem;
    }

    :host(.avatar-lg) {
      inline-size: var(--avatar-size-lg);
      block-size: var(--avatar-size-lg);
      font-size: 1.25rem;
    }

    .avatar-badge {
      position: absolute;
      inset-block-end: -2px;
      inset-inline-end: -2px;
      display: grid;
      place-items: center;
      inline-size: 1rem;
      block-size: 1rem;
      border: 2px solid var(--color-panel);
      border-radius: var(--radius-pill);
      background-color: var(--channel-other);
      color: var(--color-panel);
    }

    .avatar-badge[data-channel='WhatsApp'] {
      background-color: var(--channel-whatsapp);
    }

    .avatar-badge[data-channel='WebChat'] {
      background-color: var(--channel-web);
    }

    .avatar-badge app-icon {
      inline-size: 0.5625rem;
      block-size: 0.5625rem;
    }

    @media (forced-colors: active) {
      :host,
      .avatar-badge {
        border: 1px solid CanvasText;
      }
    }
  `,
})
export class Avatar {
  /** The person's label (a name, "Visitor 7KQ2MX", a masked number). */
  readonly name = input('');

  /** Overrides the initials: a short code or two characters chosen upstream. */
  readonly text = input('');

  /** The channel badge; none when empty or unknown. */
  readonly channel = input('');

  readonly size = input<'sm' | 'md' | 'lg'>('md');

  protected readonly letters = computed(() => this.text() || initialsOf(this.name()));

  protected readonly badge = computed<IconName | null>(
    () => BADGE_GLYPHS[this.channel() as AvatarChannel] ?? null,
  );
}
