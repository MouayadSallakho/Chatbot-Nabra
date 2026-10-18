import { DOCUMENT, inject } from '@angular/core';
import { ActivatedRouteSnapshot, Router, ViewTransitionInfo } from '@angular/router';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';

/**
 * The page a route snapshot shows, without its query string or fragment.
 *
 * Read from the snapshot tree rather than from `router.url`, whose value at the
 * moment a View Transition starts depends on the router's internal timing. The
 * primary outlet is the only one this application uses.
 */
export function pathOfSnapshot(root: ActivatedRouteSnapshot): string {
  const segments: string[] = [];

  for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
    segments.push(...route.url.map((segment) => segment.toString()));
  }

  return '/' + segments.join('/');
}

/** The same, for an address: `/customers?page=3#top` is the page `/customers`. */
export function pathOfUrl(router: Router, url: string): string {
  const segments = router.parseUrl(url).root.children['primary']?.segments ?? [];

  return '/' + segments.map((segment) => segment.toString()).join('/');
}

/**
 * Set on `<html>` for the length of a transition that must not animate
 * (styles/_base.scss turns its animations off).
 */
export const INSTANT_TRANSITION_CLASS = 'route-transition-instant';

/**
 * Decides, per navigation, whether the route crossfade runs.
 *
 * Not when only the query string or fragment changed -- the next page of a
 * list -- because the page itself stays put and a fade would read as the
 * screen reloading. Not for a reader who asked for reduced motion either (the
 * stylesheet's media query already says so; this makes it independent of
 * which rule wins). Nor between two panes of one workspace -- opening another
 * conversation while the inbox shows its list beside it (Phase 36.6) -- because
 * only one pane changes, and fading the whole content area would flash the
 * list the reader is working from. A route marks itself as such a workspace
 * with `data: { panes: '<media query>' }`, the width from which its panes sit
 * side by side; below it, its children are ordinary pages again.
 *
 * The transition is made instant rather than skipped: `skipTransition()`
 * rejects the transition's `ready` promise, which the router reports as a
 * console error in development on every page of a list. An instant transition
 * finishes on the next frame with nothing to report. Runs in an injection
 * context (the router calls it inside one).
 */
export function onRouteViewTransition({ transition, from, to }: ViewTransitionInfo): void {
  const media = inject(MEDIA_QUERY_FACTORY);
  const reduced = media(MediaQueries.reducedMotion).matches;
  const panes = sharedPaneWorkspace(from, to);
  const sideBySide = panes !== null && media(panes).matches;

  if (!reduced && !sideBySide && pathOfSnapshot(from) !== pathOfSnapshot(to)) {
    return;
  }

  const root = inject(DOCUMENT).documentElement;
  const done = () => root.classList.remove(INSTANT_TRANSITION_CLASS);

  root.classList.add(INSTANT_TRANSITION_CLASS);
  transition.finished.then(done, done);
}

/**
 * The media query of a pane workspace both routes sit in, or null.
 *
 * The outermost route carrying `data.panes` on each side, compared by its
 * configuration: children inherit their parent's data, so the first match
 * walking down is the workspace itself.
 */
function sharedPaneWorkspace(
  from: ActivatedRouteSnapshot,
  to: ActivatedRouteSnapshot,
): string | null {
  const workspace = (root: ActivatedRouteSnapshot) => {
    for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
      const panes: unknown = route.data?.['panes'];

      if (typeof panes === 'string') {
        return { config: route.routeConfig, panes };
      }
    }

    return null;
  };

  const left = workspace(from);
  const right = workspace(to);

  return left !== null && right !== null && left.config === right.config ? left.panes : null;
}
