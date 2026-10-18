import { Routes } from '@angular/router';
import { BillingApi } from './billing-api';

/**
 * The billing screen.
 *
 * `BillingApi` is provided here rather than in the root injector: this page is
 * its only consumer, so scoping it keeps it inside the lazily loaded chunk, and
 * a deployment nobody buys anything on never downloads it.
 */
export const billingRoutes: Routes = [
  {
    path: '',
    providers: [BillingApi],
    children: [
      {
        path: '',
        title: $localize`:@@title.billing:Billing`,
        loadComponent: () => import('./billing-page/billing-page').then((m) => m.BillingPage),
      },
    ],
  },
];
