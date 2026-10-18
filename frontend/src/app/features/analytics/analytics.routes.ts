import { Routes } from '@angular/router';
import { AnalyticsApi } from './analytics-api';

/**
 * The analytics screen.
 *
 * <strong>`AnalyticsApi` is provided here, not in the root injector.</strong> It
 * is used by this page and nothing else, so scoping it to the feature keeps it
 * inside the lazily loaded chunk.
 */
export const analyticsRoutes: Routes = [
  {
    path: '',
    providers: [AnalyticsApi],
    children: [
      {
        path: '',
        title: $localize`:@@title.analytics:Analytics`,
        loadComponent: () =>
          import('./analytics-dashboard/analytics-dashboard').then((m) => m.AnalyticsDashboardPage),
      },
    ],
  },
];
