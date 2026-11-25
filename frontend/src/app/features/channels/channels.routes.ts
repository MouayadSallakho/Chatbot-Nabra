import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { provideProblemWords } from '@app/core/problems/problem-words';
import { AiAgentApi } from '@app/features/ai-agents/ai-agent-api';
import { CHANNEL_PROBLEM_WORDS } from './channel-problems';
import { WebChatAdminApi } from './website-chat-api';
import { WhatsAppApi } from './whatsapp-api';

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
    providers: [
      WebChatAdminApi,
      WhatsAppApi,
      AiAgentApi,
      provideProblemWords(CHANNEL_PROBLEM_WORDS),
    ],
    children: [
      {
        path: '',
        title: $localize`:@@title.channels:Channels`,
        loadComponent: () => import('./channels-page/channels-page').then((m) => m.ChannelsPage),
      },
      {
        path: 'website-chat',
        title: $localize`:@@title.websiteChat:Website chat`,
        loadComponent: () =>
          import('./website-chat/website-chat-page/website-chat-page').then(
            (m) => m.WebsiteChatPage,
          ),
      },
      {
        // After the literal segment, so it cannot swallow it.
        path: 'website-chat/:webChatEndpointId',
        title: $localize`:@@title.websiteChatLink:Website chat link`,
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./website-chat/website-chat-detail/website-chat-detail').then(
            (m) => m.WebsiteChatDetail,
          ),
      },
      ...(typeof ngDevMode === 'undefined' || ngDevMode
        ? [
            {
              path: 'messenger-simulator',
              title: $localize`:@@ms.title:Messenger simulator`,
              loadComponent: () =>
                import('./social-simulator/social-simulator').then((m) => m.SocialSimulatorPage),
            },
            {
              path: 'instagram-simulator',
              data: { channel: 'Instagram' },
              title: $localize`:@@ig.title:Instagram Direct simulator`,
              loadComponent: () =>
                import('./social-simulator/social-simulator').then((m) => m.SocialSimulatorPage),
            },
          ]
        : []),
      {
        path: 'whatsapp',
        title: $localize`:@@title.whatsApp:WhatsApp`,
        loadComponent: () =>
          import('./whatsapp/whatsapp-page/whatsapp-page').then((m) => m.WhatsAppPage),
      },
      {
        // After the literal segment, so it cannot swallow it.
        path: 'whatsapp/:connectionId',
        title: $localize`:@@title.whatsAppNumber:WhatsApp number`,
        loadComponent: () =>
          import('./whatsapp/whatsapp-detail/whatsapp-detail').then((m) => m.WhatsAppDetail),
      },
    ],
  },
];
