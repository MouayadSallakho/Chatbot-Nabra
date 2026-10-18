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
 * which rule wins).
 *
 * The transition is made instant rather than skipped: `skipTransition()`
 * rejects the transition's `ready` promise, which the router reports as a
 * console error in development on every page of a list. An instant transition
 * finishes on the next frame with nothing to report. Runs in an injection
 * context (the router calls it inside one).
 */
export function onRouteViewTransition({ transition, from, to }: ViewTransitionInfo): void {
  const reduced = inject(MEDIA_QUERY_FACTORY)(MediaQueries.reducedMotion).matches;

  if (!reduced && pathOfSnapshot(from) !== pathOfSnapshot(to)) {
    return;
  }

  const root = inject(DOCUMENT).documentElement;
  const done = () => root.classList.remove(INSTANT_TRANSITION_CLASS);

  root.classList.add(INSTANT_TRANSITION_CLASS);
  transition.finished.then(done, done);
}
