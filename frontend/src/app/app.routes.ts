import { Routes } from '@angular/router';
import { AppShell } from '@app/layout/app-shell/app-shell';
import { HOME_PATH, NAV_ITEMS } from '@app/layout/navigation';
import { LOGIN_PATH, REGISTER_PATH, anonymousGuard, authGuard } from '@app/core/auth/auth-guard';
import { WORKSPACE_SETUP_PATH, workspaceGuard } from '@app/core/workspace/workspace-guard';
import { ACCEPT_INVITATION_PATH } from '@app/core/invitations/invitation-handoff';
import { TeamApi } from '@app/features/team/team-api';
import { HostedChatApi } from '@app/features/chat/hosted-chat-api';
import { HostedChatSessionStore } from '@app/features/chat/hosted-chat-session';
import { AnalyticsApi } from '@app/features/analytics/analytics-api';

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
    // Outside the shell and behind no guard at all. Somebody opening an
    // invitation may have no account, and somebody accepting their first one has
    // no workspace -- either guard would bounce them and lose the invitation.
    // The page itself asks them to sign in when they need to.
    path: ACCEPT_INVITATION_PATH,
    title: 'Accept invitation',
    providers: [TeamApi],
    loadComponent: () =>
      import('@app/features/invitations/accept-invitation/accept-invitation').then(
        (m) => m.AcceptInvitation,
      ),
  },
  {
    // The hosted chat page. Outside the shell and behind no guard at all: a
    // customer has no OmniAI account and never will, and the workspace guard
    // would bounce them to a setup screen that means nothing to them.
    //
    // `HostedChatApi` is provided on the route so it lives in this chunk, and so
    // no admin screen can reach the anonymous client by accident. The session
    // store is provided beside it for the same reason: it is the only thing in the
    // application that touches `sessionStorage`, and it belongs to this page alone.
    path: 'chat/:publicKey',
    title: 'Chat',
    providers: [HostedChatApi, HostedChatSessionStore],
    loadComponent: () =>
      import('@app/features/chat/hosted-chat/hosted-chat').then((m) => m.HostedChat),
  },
  {
    // The embedded widget's own document, served into an iframe on a customer's
    // website. Outside the shell and behind no guard, for the same reason the
    // hosted page is: the visitor is a member of nobody's workspace. What decides
    // whether it may run is the workspace's origin allowlist, checked on the
    // server against a website the browser reported.
    path: 'embed',
    loadChildren: () => import('@app/features/embed/embed.routes').then((m) => m.embedRoutes),
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

        // The overview shows a four-number summary from the analytics endpoint,
        // so it needs that client. Provided here rather than in the root
        // injector: two small consumers, neither of which should put it in
        // everybody's initial bundle.
        providers: [AnalyticsApi],
        loadComponent: () => import('@app/features/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'ai-agents',
        loadChildren: () =>
          import('@app/features/ai-agents/ai-agent.routes').then((m) => m.aiAgentRoutes),
      },
      {
        // Role administration, replacing the Phase 10 "Settings" placeholder.
        // Lazily loaded like the AI agent feature: somebody who never opens it
        // never downloads it.
        path: 'roles',
        loadChildren: () => import('@app/features/roles/roles.routes').then((m) => m.roleRoutes),
      },
      {
        // Team administration. Lazily loaded like the other feature areas, so
        // somebody who never opens it never downloads it.
        path: 'team',
        loadChildren: () => import('@app/features/team/team.routes').then((m) => m.teamRoutes),
      },
      {
        // The conversation inbox. Real from Phase 15 -- these are the
        // conversations customers have actually had.
        path: 'conversations',
        loadChildren: () =>
          import('@app/features/conversations/conversations.routes').then(
            (m) => m.conversationRoutes,
          ),
      },
      {
        // Knowledge: the documents agents draw on. Lazily loaded like the other
        // feature areas, so somebody who never opens it never downloads it.
        path: 'knowledge',
        loadChildren: () =>
          import('@app/features/knowledge/knowledge.routes').then((m) => m.knowledgeRoutes),
      },
      {
        // Channels, of which website chat is the first that is built.
        path: 'channels',
        loadChildren: () =>
          import('@app/features/channels/channels.routes').then((m) => m.channelRoutes),
      },
      {
        // Analytics, replacing the Phase 10 placeholder. Lazily loaded like the
        // other feature areas, so somebody who never opens it never downloads it.
        path: 'analytics',
        loadChildren: () =>
          import('@app/features/analytics/analytics.routes').then((m) => m.analyticsRoutes),
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
