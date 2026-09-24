import { Routes } from '@angular/router';

/**
 * Notifications: the full history, and the member's own email preferences.
 *
 * Lazy, and reached from the bell rather than the sidebar: it is a personal
 * area every member has, not a product area somebody is granted. There is no
 * permission to check here beyond membership, which the server enforces.
 */
export const notificationRoutes: Routes = [
  {
    path: '',
    title: 'Notifications',
    loadComponent: () =>
      import('./notifications-page/notifications-page').then((m) => m.NotificationsPage),
  },
  {
    path: 'preferences',
    title: 'Notification preferences',
    loadComponent: () =>
      import('./notification-preferences/notification-preferences').then(
        (m) => m.NotificationPreferencesPage,
      ),
  },
];
