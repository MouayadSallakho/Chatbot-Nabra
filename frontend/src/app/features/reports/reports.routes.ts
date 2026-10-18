import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { ReportsApi } from './reports-api';

/**
 * Reports: generating and downloading, and schedules.
 *
 * `ReportsApi` is provided here rather than in the root injector: these two
 * screens are its only consumers, so scoping it keeps it inside the lazily
 * loaded chunk.
 */
export const reportsRoutes: Routes = [
  {
    path: '',
    providers: [ReportsApi],
    children: [
      {
        path: '',
        title: $localize`:@@title.reports:Reports`,
        loadComponent: () => import('./reports-page/reports-page').then((m) => m.ReportsPage),
      },
      {
        path: 'schedules',
        title: $localize`:@@title.reportSchedules:Report schedules`,
        loadComponent: () =>
          import('./report-schedules/report-schedules').then((m) => m.ReportSchedules),
        // An open schedule editor with edits is asked about before it is lost.
        canDeactivate: [unsavedChangesGuard],
      },
    ],
  },
];
