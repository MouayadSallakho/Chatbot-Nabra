import { DOCUMENT, DestroyRef, InjectionToken, Signal, inject, signal } from '@angular/core';

/**
 * Media queries the application reacts to.
 *
 * Kept together so the breakpoints the TypeScript uses are visible next to each
 * other, and so a change to one is an obvious change rather than a search
 * through component files.
 */
export const MediaQueries = {
  /** The operating system's colour preference. */
  prefersDark: '(prefers-color-scheme: dark)',

  /**
   * Where the layout switches from the phone's top and bottom bars to the side
   * bar (Phase 39.3; until then, from the drawer).
   *
   * 992px is Bootstrap's `lg` breakpoint, matched deliberately: the grid classes
   * used in templates change behaviour at exactly this width, and a shell that
   * reflowed at a different one would tear the two apart.
   */
  desktop: '(min-width: 992px)',

  /**
   * Where the side bar is shown in full (Phase 39.3, plan 39.0 §3.2.4): 216px
   * with its names. Narrower desktops get the rail of icons.
   */
  sidebarFull: '(min-width: 1280px)',

  /**
   * Wide enough for the inbox's three panes (Phase 36.6): the conversation list
   * stays beside the open conversation. The inbox's stylesheets switch layout
   * at exactly this width, written in px there too, so the two never disagree.
   */
  inbox: '(min-width: 1280px)',

  /**
   * A phone: the conversation's header shrinks to one line here (Phase 38
   * note 5, 39.3), with its facts on demand.
   */
  phone: '(max-width: 767.98px)',

  /**
   * Tall enough for a conversation to fill the screen with its own scrolling
   * transcript between a header and a composer. Shorter (a landscape phone, a
   * 200% zoom) and it scrolls as an ordinary page, so nobody reads through a
   * letterbox.
   */
  tall: '(min-height: 600px)',

  /** The reader asked the operating system for less motion. */
  reducedMotion: '(prefers-reduced-motion: reduce)',
} as const;

/** Builds a `MediaQueryList` for a query. Replaced wholesale in tests. */
export type MediaQueryFactory = (query: string) => MediaQueryList;

/**
 * A `MediaQueryList` that matches nothing and never changes.
 *
 * Needed because `window.matchMedia` is genuinely absent in jsdom, which is what
 * the unit tests run in. Without this the application would throw during any
 * test that instantiates a service reading a media query -- so the fallback is
 * not defensive padding, it is the difference between a testable shell and one
 * that can only run in a browser.
 *
 * Matching nothing is the right default: it resolves to the light theme and the
 * mobile layout, both of which are the safe, fully functional branch.
 */
function inertQueryList(query: string): MediaQueryList {
  return {
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  } as MediaQueryList;
}

/**
 * How the application asks the browser about a media query.
 *
 * An injection token rather than a direct `window.matchMedia` call so that tests
 * can drive the operating system's theme and the viewport width deterministically
 * -- neither of which a test can otherwise change.
 */
export const MEDIA_QUERY_FACTORY = new InjectionToken<MediaQueryFactory>('MEDIA_QUERY_FACTORY', {
  providedIn: 'root',
  factory: () => {
    const view = inject(DOCUMENT).defaultView;

    if (!view || typeof view.matchMedia !== 'function') {
      return inertQueryList;
    }

    return (query: string) => view.matchMedia(query);
  },
});

/**
 * Tracks a media query as a signal.
 *
 * Must be called from an injection context: the subscription is released
 * through `DestroyRef`, so a component that stops existing stops listening.
 * Leaked media query listeners are the kind of thing that never shows up in
 * development and quietly accumulates in a long-lived admin session.
 */
export function mediaQuerySignal(query: string): Signal<boolean> {
  const list = inject(MEDIA_QUERY_FACTORY)(query);
  const matches = signal(list.matches);
  const onChange = (event: MediaQueryListEvent) => matches.set(event.matches);

  list.addEventListener('change', onChange);
  inject(DestroyRef).onDestroy(() => list.removeEventListener('change', onChange));

  return matches.asReadonly();
}
