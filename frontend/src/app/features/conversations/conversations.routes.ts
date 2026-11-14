import { Routes } from '@angular/router';
import { MediaQueries } from '@app/core/media/media';
import { provideProblemWords } from '@app/core/problems/problem-words';
import { WhatsAppApi } from '@app/features/channels/whatsapp-api';
import { CustomersApi } from '@app/features/customers/customers-api';
import { CONVERSATION_PROBLEM_WORDS } from './conversation-problems';
import { ConversationsApi } from './conversations-api';
import { InboxState } from './inbox-state';

/**
 * The conversation inbox (Phase 36.6: a workspace, not two pages).
 *
 * `/conversations` is the inbox itself: the list, plus a child outlet. From
 * 1280px the open conversation sits beside the list (`data.panes`), so the
 * list stays in view while somebody works one conversation after another.
 * Below that the same two routes behave as they always have: the list, and a
 * conversation page with a way back. The addresses, `?page=` and Back are
 * unchanged.
 *
 * `ConversationsApi` and `InboxState` are provided here rather than in the root
 * injector: they are used only by these screens, so scoping them keeps them in
 * the lazily loaded chunk, as every other feature area does.
 *
 * `CustomersApi` is here for one read too (39.4b): the customer panel beside an
 * open conversation shows "Customer since", the customer's conversation count
 * and their other conversations, for a reader with `customers.view`.
 *
 * `WhatsAppApi` is here for one read: a WhatsApp conversation shows who it is
 * with and whether each reply reached them, and that is channel-specific
 * information the shared conversation API deliberately does not carry.
 */
export const conversationRoutes: Routes = [
  {
    path: '',
    providers: [
      ConversationsApi,
      CustomersApi,
      WhatsAppApi,
      InboxState,
      provideProblemWords(CONVERSATION_PROBLEM_WORDS),
    ],
    data: { panes: MediaQueries.inbox },
    loadComponent: () =>
      import('./conversation-inbox/conversation-inbox').then((m) => m.ConversationInbox),
    children: [
      {
        path: '',
        // The navigation's word for this area (39.3), so the tab and the heading say it too.
        title: $localize`:@@title.inbox:Inbox`,
        loadComponent: () =>
          import('./inbox-placeholder/inbox-placeholder').then((m) => m.InboxPlaceholder),
      },
      {
        path: ':conversationId',
        title: $localize`:@@title.conversation:Conversation`,
        loadComponent: () =>
          import('./conversation-detail/conversation-detail').then((m) => m.ConversationDetail),
      },
    ],
  },
];
