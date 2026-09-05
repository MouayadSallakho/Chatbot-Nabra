import { Routes } from '@angular/router';
import { AppShell } from '@app/layout/app-shell/app-shell';
import { HOME_PATH, NAV_ITEMS } from '@app/layout/navigation';
import { WORKSPACE_SETUP_PATH, workspaceGuard } from '@app/core/workspace/workspace-guard';

/**
 * One route per unbuilt product area, generated from the navigation model.
 *
 * GENERATED RATHER THAN LISTED, which is the point. The sidebar renders the same
 * array, so a navigation entry without a route -- a dead link -- is not
 * something review has to catch. It cannot be written.
 *
 * `data.area` reaches the component as an input through
 * `withComponentInputBinding`, so the label exists in exactly one place. An area
 * marked `implemented` is skipped here and gets a real route below.
 */
const placeholderRoutes: Routes = NAV_ITEMS.filter((item) => !item.implemented).map((item) => ({
  path: item.path,
  title: item.label,
  data: { area: item.label },
  loadComponent: () => import('@app/features/placeholder/placeholder').then((m) => m.Placeholder),
}));

/**
 * Every page sits inside the shell, and every page needs a workspace.
 *
 * The setup screen is the one exception, and it is deliberately outside the
 * shell: there is no workspace yet, so a sidebar full of workspace-scoped links
 * would lead nowhere.
 *
 * Feature pages are lazily loaded. The shell and the design system are what a
 * reader needs immediately; a page they have not asked for is not.
 */
export const routes: Routes = [
  {
    path: WORKSPACE_SETUP_PATH,
    title: 'Set up workspace',
    loadComponent: () =>
      import('@app/features/workspace-setup/workspace-setup').then((m) => m.WorkspaceSetup),
  },
  {
    path: '',
    component: AppShell,
    // Resolves the development workspace before any workspace-scoped screen
    // renders, so no page has to defend against a missing tenant id.
    canActivate: [workspaceGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: HOME_PATH },
      {
        path: HOME_PATH,
        title: 'Overview',
        loadComponent: () => import('@app/features/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'ai-agents',
        loadChildren: () =>
          import('@app/features/ai-agents/ai-agent.routes').then((m) => m.aiAgentRoutes),
      },
      ...placeholderRoutes,
      // Deliberate, and deliberately not a redirect: bouncing an unknown address
      // to the overview leaves the reader unsure whether their link was wrong or
      // the product ignored it, and hides broken links from whoever should fix
      // them.
      {
        path: '**',
        title: 'Page not found',
        loadComponent: () => import('@app/features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
