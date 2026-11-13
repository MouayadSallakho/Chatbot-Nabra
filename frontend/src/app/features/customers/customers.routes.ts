import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';
import { provideProblemWords } from '@app/core/problems/problem-words';
import { CUSTOMER_PROBLEM_WORDS } from './customer-problems';
import { CustomersApi } from './customers-api';

/**
 * The customer directory and one customer's page.
 *
 * `CustomersApi` is provided here rather than in the root injector: these two
 * screens are its only consumers, so scoping it keeps it inside the lazily
 * loaded chunk.
 */
export const customersRoutes: Routes = [
  {
    path: '',
    providers: [CustomersApi, provideProblemWords(CUSTOMER_PROBLEM_WORDS)],
    children: [
      {
        path: '',
        title: $localize`:@@title.customers:Customers`,
        loadComponent: () => import('./customer-list/customer-list').then((m) => m.CustomerList),
      },
      {
        path: ':customerId',
        title: $localize`:@@title.customer:Customer`,
        // A typed and unsaved name is asked about before it is lost, as in
        // every other editor (Phase 36.6).
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () =>
          import('./customer-detail/customer-detail').then((m) => m.CustomerDetail),
      },
    ],
  },
];
