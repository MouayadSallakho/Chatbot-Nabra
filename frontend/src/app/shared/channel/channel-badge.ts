import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import type { IconName } from '@app/shared/icon/icon-paths';

/** A channel a conversation or a customer arrived on, as the API names it. */
export type Channel = 'WebChat' | 'WhatsApp' | 'Instagram' | 'Messenger';

/**
 * The word for each channel, in the reader's language.
 *
 * Written once, here, so a channel is called the same thing in the inbox, the
 * customer directory and a customer's page. Arabic follows the glossary
 * (`src/locale/GLOSSARY.md`): Meta's own Arabic forms for its products.
 */
export const CHANNEL_WORDS: Readonly<Record<Channel, string>> = {
  WebChat: $localize`:@@channel.webChat:Website chat`,
  WhatsApp: $localize`:@@channel.whatsApp:WhatsApp`,
  Instagram: $localize`:@@channel.instagram:Instagram`,
  Messenger: $localize`:@@channel.messenger:Messenger`,
};

const CHANNEL_GLYPHS: Readonly<Record<Channel, IconName>> = {
  WebChat: 'website',
  WhatsApp: 'whatsapp',
  Instagram: 'instagram',
  Messenger: 'messenger',
};

/** The word for a channel; an unknown value is shown as the API sent it, never hidden. */
export function channelWord(channel: string): string {
  return CHANNEL_WORDS[channel as Channel] ?? channel;
}

/**
 * A channel, as a glyph and its name.
 *
 * Never the glyph alone and never a brand colour (MASTER.md section 6): the
 * glyphs are generic shapes, and the word is what carries the meaning.
 */
@Component({
  selector: 'app-channel-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  host: { class: 'channel-badge' },
  template: `
    <app-icon class="channel-badge-glyph" [name]="glyph()" [strokeWidth]="1.6" />
    <span>{{ word() }}</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      min-inline-size: 0;
      color: var(--text-secondary);
      font: var(--text-small);
      white-space: nowrap;
    }

    .channel-badge-glyph {
      flex: none;
      inline-size: 1rem;
      block-size: 1rem;
      color: var(--text-muted);
    }
  `,
})
export class ChannelBadge {
  readonly channel = input.required<string>();

  protected readonly word = computed(() => channelWord(this.channel()));

  protected readonly glyph = computed<IconName>(
    () => CHANNEL_GLYPHS[this.channel() as Channel] ?? 'channels',
  );
}
