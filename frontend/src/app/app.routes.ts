import { Routes } from '@angular/router';
import { AppShell } from '@app/layout/app-shell/app-shell';
import { HOME_PATH, NAV_ITEMS } from '@app/layout/navigation';

/**
 * One route per unbuilt product area, generated from the navigation model.
 *
 * GENERATED RATHER THAN LISTED, which is the point. The sidebar renders the same
 * array, so a navigation entry without a route -- a dead link -- is not
 * something review has to catch. It cannot be written.
 *
 * `data.area` reaches the component as an input through
 * `withComponentInputBinding`, so the label exists in exactly one place.
 */
const placeholderRoutes: Routes = NAV_ITEMS.filter((item) => !item.implemented).map((item) => ({
  path: item.path,
  title: item.label,
  data: { area: item.label },
  loadComponent: () => import('@app/features/placeholder/placeholder').then((m) => m.Placeholder),
}));

/**
 * Every page sits inside the shell.
 *
 * The shell is a layout route with no path of its own, so navigation, the top
 * bar and the theme control persist across navigations rather than being torn
 * down and rebuilt on each one.
 *
 * Feature pages are lazily loaded. The shell and the design system are what a
 * reader needs immediately; a page they have not asked for is not.
 */
export const routes: Routes = [
  {
    path: '',
    component: AppShell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: HOME_PATH },
      {
        path: HOME_PATH,
        title: 'Overview',
        loadComponent: () => import('@app/features/overview/overview').then((m) => m.Overview),
      },
      ...placeholderRoutes,
      // Deliberate, and deliberately not a redirect: bouncing an unknown address
      // to the overview leaves the reader unsure whether their link was wrong or
      // the product ignored it, and hides broken links from whoever should fix
      // them.
      {
        path: '**',
        title: 'Page not found',
        loadComponent: () => import('@app/features/not-found/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
