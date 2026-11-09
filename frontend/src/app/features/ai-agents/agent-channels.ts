import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WebChatAdminApi, WebChatWidget } from '@app/features/channels/website-chat-api';
import type { AiAgentChannel } from './ai-agent-api';

/** One channel an agent answers on, ready for a list or a link. */
export interface AgentChannelUse {
  readonly channel: 'WebChat' | 'WhatsApp';
  readonly id: string;
  /** The channel's own status (`Draft`, `Active`, `Disabled`), not the agent's. */
  readonly status: string;
  /** Where the channel is configured. */
  readonly link: readonly string[];
}

/** Where each kind of channel is configured. */
const CHANNEL_PAGES: Readonly<Record<AgentChannelUse['channel'], string>> = {
  WebChat: '/channels/website-chat',
  WhatsApp: '/channels/whatsapp',
};

/**
 * The channels an agent read named, ready to show (Phase 38.3).
 *
 * The server includes only the kinds this reader may see. A kind this client does
 * not know has no page to link to, so it is left out rather than linked nowhere.
 */
export function visibleChannels(channels: readonly AiAgentChannel[]): readonly AgentChannelUse[] {
  return channels
    .filter((channel) => channel.kind in CHANNEL_PAGES)
    .map((channel) => ({
      channel: channel.kind,
      id: channel.id,
      status: channel.status,
      link: [CHANNEL_PAGES[channel.kind], channel.id],
    }));
}

/** The one channel read the agent screens still make: the widget's look, for the preview. */
@Injectable()
export class AgentChannels {
  private readonly webChat = inject(WebChatAdminApi);
  private readonly permissions = inject(PermissionService);

  /**
   * The widget appearance customers see for this agent, or null.
   *
   * Taken from the agent's website chat (an active one first), so the preview
   * wears the workspace's real accent and greeting. Null when the agent is on
   * no website chat, the reader may not see website chat, or it cannot be read:
   * the preview then says it is using the widget's default look.
   */
  async widgetFor(
    tenantId: string,
    uses: readonly AgentChannelUse[],
  ): Promise<WebChatWidget | null> {
    const endpoints = uses.filter((use) => use.channel === 'WebChat');
    const endpoint = endpoints.find((use) => use.status === 'Active') ?? endpoints[0];

    if (!endpoint || !this.permissions.has('website-chat.view')) {
      return null;
    }

    try {
      return (await firstValueFrom(this.webChat.getWidget(tenantId, endpoint.id))).value.widget;
    } catch {
      return null;
    }
  }
}

/** A channel's own status, when it is not simply on: channels are feminine in Arabic. */
export function channelStatusNote(status: string): string | null {
  switch (status) {
    case 'Active':
      return null;
    case 'Draft':
      return $localize`:@@agents.channel.draft:Draft`;
    case 'Disabled':
      return $localize`:@@agents.channel.disabled:Disabled`;
    default:
      return status;
  }
}
