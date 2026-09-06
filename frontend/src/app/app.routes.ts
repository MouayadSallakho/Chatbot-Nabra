import { Routes } from '@angular/router';
import { AppShell } from '@app/layout/app-shell/app-shell';
import { HOME_PATH, NAV_ITEMS } from '@app/layout/navigation';
import { LOGIN_PATH, REGISTER_PATH, anonymousGuard, authGuard } from '@app/core/auth/auth-guard';
import { WORKSPACE_SETUP_PATH, workspaceGuard } from '@app/core/workspace/workspace-guard';

/**
 * One route per unbuilt product area, generated from the navigation model.
 *
 * Generated rather than listed, so a navigation entry can never point at a route
 * that does not exist. That mismatch is the classic way a sidebar link 404s, and
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
 * Three tiers, and the order of the guards on each is the design.
 *
 * <strong>Anonymous.</strong> Sign-in and registration, behind `anonymousGuard`
 * so a signed-in reader is not shown a form that would sign them in as
 * themselves again.
 *
 * <strong>Authenticated, no workspace yet.</strong> The workspace screen. It sits
 * outside the shell deliberately: a sidebar full of workspace-scoped links would
 * lead nowhere when no workspace is selected.
 *
 * <strong>Authenticated, inside a workspace.</strong> Everything else, behind
 * `authGuard` and then `workspaceGuard` — in that order, because asking which
 * workspaces somebody has only makes sense once there is a somebody.
 *
 * <strong>None of this is the security boundary.</strong> A route guard runs in
 * a browser its reader controls and can always be defeated by them. Every
 * request is authenticated on the server and every tenant-scoped one is checked
 * against a membership row; these guards exist so the right screen appears, not
 * to protect data.
 */
export const routes: Routes = [
  {
    path: LOGIN_PATH,
    title: 'Sign in',
    canActivate: [anonymousGuard],
    loadComponent: () => import('@app/features/auth/login/login').then((m) => m.Login),
  },
  {
    path: REGISTER_PATH,
    title: 'Create account',
    canActivate: [anonymousGuard],
    loadComponent: () => import('@app/features/auth/register/register').then((m) => m.Register),
  },
  {
    path: WORKSPACE_SETUP_PATH,
    title: 'Choose workspace',
    canActivate: [authGuard],
    loadComponent: () =>
      import('@app/features/workspace-setup/workspace-setup').then((m) => m.WorkspaceSetup),
  },
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard, workspaceGuard],
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
      // the page moved.
      {
        path: '**',
        title: 'Page not found',
        loadComponent: () => import('@app/features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
