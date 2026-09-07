import { Routes } from '@angular/router';
import { ConversationsApi } from './conversations-api';

/**
 * The conversation inbox.
 *
 * `ConversationsApi` is provided here rather than in the root injector: it is
 * used only by these pages, so scoping it to the feature keeps it inside the
 * lazily loaded chunk, as every other feature area does.
 */
export const conversationRoutes: Routes = [
  {
    path: '',
    providers: [ConversationsApi],
    children: [
      {
        path: '',
        title: 'Conversations',
        loadComponent: () =>
          import('./conversation-list/conversation-list').then((m) => m.ConversationList),
      },
      {
        path: ':conversationId',
        title: 'Conversation',
        loadComponent: () =>
          import('./conversation-detail/conversation-detail').then((m) => m.ConversationDetail),
      },
    ],
  },
];
