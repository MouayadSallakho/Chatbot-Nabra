import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { provideProblemWords } from '@app/core/problems/problem-words';
import { WORKSPACE_PROBLEM_WORDS } from '@app/features/workspace-setup/workspace-problems';

/**
 * The workspace's own settings (Phase 38.4): its name, and from 38.5 its time
 * zone.
 *
 * The workspace refusals are the same words the create-a-workspace page uses,
 * registered here so they arrive with this route as well.
 *
 * <strong>No route guard.</strong> Every member with `workspace.view` reads the
 * page (the owner's decision, 38.0 §4.3 #7e) and only `workspace.manage` edits;
 * the page explains both, and the server decides regardless.
 */
export const workspaceSettingsRoutes: Routes = [
  {
    path: '',
    providers: [provideProblemWords(WORKSPACE_PROBLEM_WORDS)],
    children: [
      {
        path: '',
        canDeactivate: [unsavedChangesGuard],
        title: $localize`:@@title.workspaceSettings:Workspace settings`,
        loadComponent: () =>
          import('./workspace-settings-page/workspace-settings-page').then(
            (m) => m.WorkspaceSettingsPage,
          ),
      },
    ],
  },
];
