import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '@app/core/navigation/unsaved-changes';

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
    title: $localize`:@@title.notifications:Notifications`,
    loadComponent: () =>
      import('./notifications-page/notifications-page').then((m) => m.NotificationsPage),
  },
  {
    path: 'preferences',
    // Like every other editor: leaving with unsaved choices asks first.
    canDeactivate: [unsavedChangesGuard],
    title: $localize`:@@title.notificationPreferences:Notification preferences`,
    loadComponent: () =>
      import('./notification-preferences/notification-preferences').then(
        (m) => m.NotificationPreferencesPage,
      ),
  },
];
