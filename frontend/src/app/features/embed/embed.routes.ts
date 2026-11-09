import { Routes } from '@angular/router';
import { provideProblemWords } from '@app/core/problems/problem-words';
import { CHAT_PROBLEM_WORDS } from '@app/shared/chat/chat-problems';
import { EmbedBridge } from './embed-bridge';
import { EmbedChatApi } from './embed-chat-api';
import { EmbedSessionStore } from './embed-session';

/**
 * The document served into the iframe on a customer's website.
 *
 * <strong>Outside the shell and behind no guard.</strong> A visitor to somebody
 * else's website has no OmniAI account and never will. Whether the widget may run
 * is decided by the workspace's origin allowlist on the server, against a website
 * the browser reported — not by anything in this file.
 *
 * <strong>Its three services are provided here, not in the root injector.</strong>
 * They live in this lazily loaded chunk, so the admin application never downloads
 * them; and nothing outside this route can reach the embedded client, the bridge,
 * or the store that holds a conversation credential. That last one is the point:
 * the credential has exactly one home, and it is reachable from exactly one
 * screen.
 */
export const embedRoutes: Routes = [
  {
    path: ':publicKey',
    title: $localize`:@@title.chat:Chat`,
    providers: [
      EmbedChatApi,
      EmbedSessionStore,
      EmbedBridge,
      provideProblemWords(CHAT_PROBLEM_WORDS),
    ],
    loadComponent: () => import('./embed-chat/embed-chat').then((m) => m.EmbedChat),
  },
];
