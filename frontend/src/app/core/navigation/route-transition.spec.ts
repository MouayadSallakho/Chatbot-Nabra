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

describe('route transition', () => {
  let reduced: boolean;

  beforeEach(() => {
    reduced = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: MEDIA_QUERY_FACTORY,
          useFactory: () => new FakeMediaMatcher({ [MediaQueries.reducedMotion]: reduced }).factory,
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
});
