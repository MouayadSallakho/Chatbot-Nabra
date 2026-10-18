import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { TeamApi } from './team-api';

/**
 * The team administration screens.
 *
 * <strong>`TeamApi` is provided here, not in the root injector.</strong> It is
 * used only by these pages and by the acceptance page, which provides it for
 * itself — so scoping it to the feature keeps it inside the lazily loaded chunk.
 * The same reasoning the roles and AI agent features already follow.
 *
 * <strong>No route guard.</strong> The sidebar hides this area from anybody
 * without `members.view`, and the pages explain a refusal if somebody arrives by
 * typing the URL. A guard would move the same decision earlier in a browser the
 * reader controls, while adding a second place where the permission is named.
 */
export const teamRoutes: Routes = [
  {
    path: '',
    providers: [TeamApi],
    children: [
      {
        path: '',
        title: $localize`:@@title.team:Team`,
        loadComponent: () => import('./team-page/team-page').then((m) => m.TeamPage),
      },
      {
        path: 'invite',
        title: $localize`:@@title.inviteMember:Invite member`,
        loadComponent: () => import('./invite-member/invite-member').then((m) => m.InviteMember),
      },
      {
        // Last, so it cannot swallow 'invite'. Route order is the only thing
        // separating a literal segment from a parameter that would match it.
        path: ':userId',
        canDeactivate: [unsavedChangesGuard],
        title: $localize`:@@title.teamMember:Team member`,
        loadComponent: () => import('./member-detail/member-detail').then((m) => m.MemberDetail),
      },
    ],
  },
];
