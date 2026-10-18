import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  UrlSegment,
  ViewTransitionInfo,
  provideRouter,
} from '@angular/router';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { FakeMediaMatcher } from '@app/testing/test-doubles';
import {
  INSTANT_TRANSITION_CLASS,
  onRouteViewTransition,
  pathOfSnapshot,
  pathOfUrl,
} from './route-transition';

/** A root snapshot for `/a/b`, as the router hands it over: one segment per level. */
function snapshot(...paths: string[]): ActivatedRouteSnapshot {
  let child: ActivatedRouteSnapshot | null = null;

  for (const path of [...paths].reverse()) {
    child = {
      url: [new UrlSegment(path, {})],
      firstChild: child,
    } as unknown as ActivatedRouteSnapshot;
  }

  return { url: [], firstChild: child } as unknown as ActivatedRouteSnapshot;
}

/**
 * `/conversations/<id>` under a workspace route that lays its children out as
 * panes from `MediaQueries.inbox`: the shape the inbox's routes have. The
 * workspace's configuration object is shared, as the router's would be.
 */
const inboxConfig = { path: '' };

function inbox(...ids: string[]): ActivatedRouteSnapshot {
  const child = ids.length
    ? ({
        url: [new UrlSegment(ids[0], {})],
        data: { panes: MediaQueries.inbox },
        firstChild: null,
      } as unknown)
    : null;
  const workspace = {
    url: [],
    data: { panes: MediaQueries.inbox },
    routeConfig: inboxConfig,
    firstChild: child,
  };
  const feature = { url: [new UrlSegment('conversations', {})], data: {}, firstChild: workspace };

  return { url: [], data: {}, firstChild: feature } as unknown as ActivatedRouteSnapshot;
}

describe('route transition', () => {
  let reduced: boolean;
  let wide: boolean;

  beforeEach(() => {
    reduced = false;
    wide = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: MEDIA_QUERY_FACTORY,
          useFactory: () =>
            new FakeMediaMatcher({
              [MediaQueries.reducedMotion]: reduced,
              [MediaQueries.inbox]: wide,
            }).factory,
        },
      ],
    });
  });

  /**
   * Runs the hook for a navigation and reports whether that transition was
   * made instant -- checked while it runs, and that the mark is gone after.
   */
  async function instant(
    from: ActivatedRouteSnapshot,
    to: ActivatedRouteSnapshot,
  ): Promise<boolean> {
    let finish!: () => void;
    const finished = new Promise<void>((resolve) => (finish = resolve));
    const skipTransition = vi.fn();
    const info = {
      transition: { finished, skipTransition },
      from,
      to,
    } as unknown as ViewTransitionInfo;
    const root = document.documentElement;

    TestBed.runInInjectionContext(() => onRouteViewTransition(info));

    const marked = root.classList.contains(INSTANT_TRANSITION_CLASS);

    finish();
    await finished;
    await Promise.resolve();

    expect(root.classList.contains(INSTANT_TRANSITION_CLASS)).toBe(false);
    // Never skipped: a skipped transition rejects `ready`, which the router logs.
    expect(skipTransition).not.toHaveBeenCalled();

    return marked;
  }

  it('reads the page a snapshot shows, without query or fragment', () => {
    expect(pathOfSnapshot(snapshot('customers', '42'))).toBe('/customers/42');
    expect(pathOfSnapshot(snapshot())).toBe('/');
  });

  it('reads the page an address shows, without query or fragment', () => {
    const router = TestBed.inject(Router);

    expect(pathOfUrl(router, '/customers?page=3#top')).toBe('/customers');
    expect(pathOfUrl(router, '/ai-agents/new')).toBe('/ai-agents/new');
  });

  it('crossfades between two pages', async () => {
    expect(await instant(snapshot('overview'), snapshot('knowledge'))).toBe(false);
  });

  it('does not crossfade when only the query string or fragment changes', async () => {
    // A list's next page: same snapshot path, different `?page=`.
    expect(await instant(snapshot('customers'), snapshot('customers'))).toBe(true);
  });

  it('does not crossfade for a reader who asked for reduced motion', async () => {
    reduced = true;

    expect(await instant(snapshot('overview'), snapshot('knowledge'))).toBe(true);
  });

  it('does not crossfade between two panes of one workspace while they sit side by side', async () => {
    // Opening another conversation beside the list changes one pane; fading the
    // whole content area would flash the list the reader is working from.
    wide = true;

    expect(await instant(inbox(), inbox('c1'))).toBe(true);
    expect(await instant(inbox('c1'), inbox('c2'))).toBe(true);
  });

  it('crossfades between the same two routes when the panes are pages', async () => {
    // Below the workspace's width the list and the conversation are two pages.
    expect(await instant(inbox(), inbox('c1'))).toBe(false);
  });
});
