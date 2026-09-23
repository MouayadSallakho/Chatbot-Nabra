import { Routes } from '@angular/router';
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
    providers: [CustomersApi],
    children: [
      {
        path: '',
        title: 'Customers',
        loadComponent: () => import('./customer-list/customer-list').then((m) => m.CustomerList),
      },
      {
        path: ':customerId',
        title: 'Customer',
        loadComponent: () =>
          import('./customer-detail/customer-detail').then((m) => m.CustomerDetail),
      },
    ],
  },
];
