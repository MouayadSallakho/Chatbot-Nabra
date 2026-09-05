import { Routes } from '@angular/router';
import { AiAgentApi } from './ai-agent-api';

/**
 * The AI agent screens.
 *
 * <strong>`AiAgentApi` is provided here, not in the root injector.</strong> It
 * is only ever used by these three pages, so scoping it to the feature keeps it
 * inside the lazily loaded chunk: an operator who never opens AI Agents never
 * downloads the client for it.
 */
export const aiAgentRoutes: Routes = [
  {
    path: '',
    providers: [AiAgentApi],
    children: [
      {
        path: '',
        title: 'AI Agents',
        loadComponent: () => import('./agent-list/agent-list').then((m) => m.AgentList),
      },
      {
        path: 'new',
        title: 'New AI agent',
        loadComponent: () => import('./agent-create/agent-create').then((m) => m.AgentCreate),
      },
      {
        // Last, so it cannot swallow 'new'. Route order is the only thing
        // separating a literal segment from a parameter that would match it.
        path: ':aiAgentId',
        title: 'AI agent',
        loadComponent: () => import('./agent-editor/agent-editor').then((m) => m.AgentEditor),
      },
    ],
  },
];
