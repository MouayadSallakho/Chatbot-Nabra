import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { RoleApi } from '@app/core/authorization/role-api';

/**
 * The role administration screens.
 *
 * <strong>`RoleApi` is provided here, not in the root injector.</strong> It is
 * only ever used by these pages, so scoping it to the feature keeps it inside
 * the lazily loaded chunk: somebody who never opens Roles and permissions never
 * downloads the client for it. The same reasoning the AI agent feature already
 * follows.
 *
 * <strong>No route guard.</strong> The sidebar hides this area from anybody
 * without `roles.view`, and the pages themselves explain a refusal if somebody
 * arrives by typing the URL. A guard would only move the same decision earlier
 * in a browser the reader controls, while adding a second place where the
 * permission is named.
 */
export const roleRoutes: Routes = [
  {
    path: '',
    providers: [RoleApi],
    children: [
      {
        path: '',
        title: 'Roles & permissions',
        loadComponent: () => import('./role-list/role-list').then((m) => m.RoleList),
      },
      {
        path: 'new',
        title: 'New role',
        loadComponent: () => import('./role-create/role-create').then((m) => m.RoleCreate),
      },
      {
        // Last, so it cannot swallow 'new'. Route order is the only thing
        // separating a literal segment from a parameter that would match it.
        path: ':roleId',
        canDeactivate: [unsavedChangesGuard],
        title: 'Role',
        loadComponent: () => import('./role-editor/role-editor').then((m) => m.RoleEditor),
      },
    ],
  },
];
