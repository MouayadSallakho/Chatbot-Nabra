import { Routes } from '@angular/router';
import { KnowledgeApi } from './knowledge-api';

/**
 * The knowledge screens.
 *
 * <strong>`KnowledgeApi` is provided here, not in the root injector.</strong> It
 * is only used by these pages, so scoping it to the feature keeps it inside the
 * lazily loaded chunk: somebody who never opens Knowledge never downloads the
 * client for it.
 */
export const knowledgeRoutes: Routes = [
  {
    path: '',
    providers: [KnowledgeApi],
    children: [
      {
        path: '',
        title: 'Knowledge',
        loadComponent: () => import('./knowledge-list/knowledge-list').then((m) => m.KnowledgeList),
      },
      {
        path: 'new',
        title: 'New knowledge document',
        loadComponent: () =>
          import('./knowledge-create/knowledge-create').then((m) => m.KnowledgeCreate),
      },
      {
        path: 'upload',
        title: 'Upload a knowledge file',
        loadComponent: () =>
          import('./knowledge-upload/knowledge-upload').then((m) => m.KnowledgeUpload),
      },
      {
        // Last, so it cannot swallow 'new'. Route order is the only thing
        // separating a literal segment from a parameter that would match it, and
        // 'upload' is now a second literal that would otherwise be read as an id.
        path: ':knowledgeDocumentId',
        title: 'Knowledge document',
        loadComponent: () =>
          import('./knowledge-editor/knowledge-editor').then((m) => m.KnowledgeEditor),
      },
    ],
  },
];
