/**
 * Channels' words, written once (Phase 36.9).
 *
 * A website chat link and a WhatsApp number share the server's lifecycle,
 * `Draft`, `Active`, `Disabled`. Arabic adjectives agree with "channel" (قناة,
 * feminine), so these are channel messages rather than the agents' ones
 * (`src/locale/GLOSSARY.md`).
 */
export type ChannelStatus = 'Draft' | 'Active' | 'Disabled';

const STATUS_WORDS: Readonly<Record<ChannelStatus, string>> = {
  Draft: $localize`:@@channels.status.draft:Draft`,
  Active: $localize`:@@channels.status.active:Active`,
  Disabled: $localize`:@@channels.status.disabled:Disabled`,
};

/** A status in words; an unknown value is shown as the server sent it, never hidden. */
export function channelStatusWord(status: string): string {
  return STATUS_WORDS[status as ChannelStatus] ?? status;
}

/**
 * The chip classes for a status: each has its own marker as well as its word,
 * so none is told apart by colour (a tick for Active, a hollow ring for a
 * Draft, a filled dot for Disabled), as on the agents' pages.
 */
export function channelStatusTone(status: string): string {
  switch (status) {
    case 'Active':
      return 'chip-success';
    case 'Draft':
      return 'chip-neutral chip-draft';
    default:
      return 'chip-neutral';
  }
}

/** What a website chat link's status means for visitors. */
export function webChatStatusSentence(status: string): string {
  switch (status) {
    case 'Active':
      return $localize`:@@websiteChat.status.active.sentence:Published. Anybody with the link, and the widget on the websites you allow, can start a conversation.`;
    case 'Draft':
      return $localize`:@@websiteChat.status.draft.sentence:Not published yet. Visitors who open the link are told the chat is not available.`;
    default:
      return $localize`:@@websiteChat.status.disabled.sentence:Disabled. Nobody can start a conversation until it is published again; existing transcripts stay readable.`;
  }
}

/** What a WhatsApp number's status means for customers. */
export function whatsAppStatusSentence(status: string): string {
  switch (status) {
    case 'Active':
      return $localize`:@@whatsapp.status.active.sentence:In service. Messages to this number are answered by its AI agent.`;
    case 'Draft':
      return $localize`:@@whatsapp.status.draft.sentence:Not in service yet. Messages to this number are not answered.`;
    default:
      return $localize`:@@whatsapp.status.disabled.sentence:Out of service. Messages are not answered until it is put back into service; nothing is deleted.`;
  }
}

/** The lifecycle, in order: what Publish and Disable move between. */
export const CHANNEL_LIFECYCLE: readonly ChannelStatus[] = ['Draft', 'Active', 'Disabled'];

export const LIFECYCLE_LABEL = $localize`:@@channels.lifecycle.label:Lifecycle: Draft, then Active, which can be Disabled and made Active again`;
