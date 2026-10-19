import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WebChatAdminApi, WebChatWidget } from '@app/features/channels/website-chat-api';
import { WhatsAppApi } from '@app/features/channels/whatsapp-api';

/** One channel an agent answers on, as the channel lists publish it. */
export interface AgentChannelUse {
  readonly channel: 'WebChat' | 'WhatsApp';
  readonly id: string;
  /** The channel's own status (`Draft`, `Active`, `Disabled`), not the agent's. */
  readonly status: string;
  /** Where the channel is configured. */
  readonly link: readonly string[];
}

/** Which channels use which agent, and how much of that could be checked. */
export interface AgentChannelMap {
  readonly byAgent: ReadonlyMap<string, readonly AgentChannelUse[]>;
  /** Whether the reader may see at least one kind of channel. */
  readonly visible: boolean;
  /** A channel list the reader may see could not be read, so the map is partial. */
  readonly incomplete: boolean;
}

/**
 * Which channels use each agent, joined on the client (Phase 36.8).
 *
 * The agent API does not say where an agent answers; the website chat and
 * WhatsApp lists each name the agent behind every endpoint. Both lists are
 * whole (not paged) and small, so reading them beside a page of agents is two
 * cheap requests. Each is read only with its own permission: somebody who may
 * not see WhatsApp is never told which agent answers on it. A list that fails
 * marks the result incomplete rather than reading as "no channels".
 */
@Injectable()
export class AgentChannels {
  private readonly webChat = inject(WebChatAdminApi);
  private readonly whatsApp = inject(WhatsAppApi);
  private readonly permissions = inject(PermissionService);

  async load(tenantId: string): Promise<AgentChannelMap> {
    const canWebChat = this.permissions.has('website-chat.view');
    const canWhatsApp = this.permissions.has('whatsapp.view');
    const uses: AgentChannelUse[] = [];
    const owners: string[] = [];
    let incomplete = false;

    const [webChat, whatsApp] = await Promise.allSettled([
      canWebChat ? firstValueFrom(this.webChat.list(tenantId)) : Promise.resolve([]),
      canWhatsApp ? firstValueFrom(this.whatsApp.list(tenantId)) : Promise.resolve([]),
    ]);

    if (webChat.status === 'fulfilled') {
      for (const endpoint of webChat.value) {
        owners.push(endpoint.aiAgentId);
        uses.push({
          channel: 'WebChat',
          id: endpoint.id,
          status: endpoint.status,
          link: ['/channels/website-chat', endpoint.id],
        });
      }
    } else {
      incomplete = true;
    }

    if (whatsApp.status === 'fulfilled') {
      for (const connection of whatsApp.value) {
        owners.push(connection.aiAgentId);
        uses.push({
          channel: 'WhatsApp',
          id: connection.id,
          status: connection.status,
          link: ['/channels/whatsapp', connection.id],
        });
      }
    } else {
      incomplete = true;
    }

    const byAgent = new Map<string, AgentChannelUse[]>();

    uses.forEach((use, index) => {
      const list = byAgent.get(owners[index]) ?? [];

      list.push(use);
      byAgent.set(owners[index], list);
    });

    return { byAgent, visible: canWebChat || canWhatsApp, incomplete };
  }

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
