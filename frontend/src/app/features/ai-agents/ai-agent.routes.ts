import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { KnowledgeApi } from '@app/features/knowledge/knowledge-api';
import { AiAgentApi } from './ai-agent-api';

/**
 * The AI agent screens.
 *
 * <strong>`AiAgentApi` is provided here, not in the root injector.</strong> It
 * is only ever used by these three pages, so scoping it to the feature keeps it
 * inside the lazily loaded chunk: an operator who never opens AI Agents never
 * downloads the client for it.
 *
 * <strong>`KnowledgeApi` is provided beside it, and has to be.</strong> The
 * editor hosts the knowledge section, which reads and writes what this agent may
 * draw on -- so the client has to be reachable from this route as well as from
 * the knowledge one. Two instances rather than a shared singleton: it holds no
 * state, and moving it to the root injector would pull it into the initial
 * bundle for everybody who never opens either area.
 */
export const aiAgentRoutes: Routes = [
  {
    path: '',
    providers: [AiAgentApi, KnowledgeApi],
    children: [
      {
        path: '',
        title: $localize`:@@title.aiAgents:AI Agents`,
        loadComponent: () => import('./agent-list/agent-list').then((m) => m.AgentList),
      },
      {
        path: 'new',
        title: $localize`:@@title.newAiAgent:New AI agent`,
        loadComponent: () => import('./agent-create/agent-create').then((m) => m.AgentCreate),
      },
      {
        // Last, so it cannot swallow 'new'. Route order is the only thing
        // separating a literal segment from a parameter that would match it.
        path: ':aiAgentId',
        canDeactivate: [unsavedChangesGuard],
        title: $localize`:@@title.aiAgent:AI agent`,
        loadComponent: () => import('./agent-editor/agent-editor').then((m) => m.AgentEditor),
      },
    ],
  },
];
