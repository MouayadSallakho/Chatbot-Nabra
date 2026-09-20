import { Routes } from '@angular/router';
import { PlanUsageApi } from './plan-usage-api';

/**
 * The plan and usage screen.
 *
 * `PlanUsageApi` is provided here rather than in the root injector: this page is
 * its only consumer, so scoping it keeps it inside the lazily loaded chunk.
 */
export const planUsageRoutes: Routes = [
  {
    path: '',
    providers: [PlanUsageApi],
    children: [
      {
        path: '',
        title: 'Plan & usage',
        loadComponent: () =>
          import('./plan-usage-page/plan-usage-page').then((m) => m.PlanUsagePage),
      },
    ],
  },
];
