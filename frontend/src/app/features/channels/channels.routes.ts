import { Routes } from '@angular/router';
import { AiAgentApi } from '@app/features/ai-agents/ai-agent-api';
import { WebChatAdminApi } from './website-chat-api';

/**
 * The channel administration screens.
 *
 * <strong>Both API services are provided here, not in the root injector.</strong>
 * They are used only by these pages, so scoping them to the feature keeps them
 * inside the lazily loaded chunk — the same reasoning the roles, agents and team
 * features already follow. `AiAgentApi` is here because publishing a chat link
 * means choosing an agent, and that list comes from the agents API.
 *
 * <strong>No route guard.</strong> The sidebar hides this area from anybody
 * without `website-chat.view`, and the pages explain a refusal if somebody
 * arrives by typing the URL. A guard would move the same decision into a browser
 * the reader controls, while adding a second place where the permission is named.
 */
export const channelRoutes: Routes = [
  {
    path: '',
    providers: [WebChatAdminApi, AiAgentApi],
    children: [
      {
        path: '',
        title: 'Channels',
        loadComponent: () => import('./channels-page/channels-page').then((m) => m.ChannelsPage),
      },
      {
        path: 'website-chat',
        title: 'Website chat',
        loadComponent: () =>
          import('./website-chat/website-chat-page/website-chat-page').then(
            (m) => m.WebsiteChatPage,
          ),
      },
      {
        // After the literal segment, so it cannot swallow it.
        path: 'website-chat/:webChatEndpointId',
        title: 'Website chat link',
        loadComponent: () =>
          import('./website-chat/website-chat-detail/website-chat-detail').then(
            (m) => m.WebsiteChatDetail,
          ),
      },
    ],
  },
];
