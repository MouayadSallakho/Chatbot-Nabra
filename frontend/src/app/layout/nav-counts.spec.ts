import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { NavCounts } from './nav-counts';

/**
 * The navigation's count is the inbox reader's, never a read of its own: showing
 * it tells the reader so, and stopping tells it again.
 */
describe('NavCounts', () => {
  let count: ReturnType<typeof signal<number | null>>;
  let showing: number;

  beforeEach(() => {
    count = signal<number | null>(null);
    showing = 0;

    TestBed.configureTestingModule({
      providers: [
        {
          provide: InboxSignals,
          useValue: {
            count,
            showCount: () => {
              showing += 1;

              return () => (showing -= 1);
            },
          },
        },
      ],
    });
  });

  /** The reader is a lazy chunk: wait until it has been asked (or not) to show. */
  const loaded = (expected = 1) => vi.waitFor(() => expect(showing).toBe(expected));

  it('is unknown until the reader has loaded and counted', async () => {
    const counts = TestBed.inject(NavCounts);

    expect(counts.needsPerson()).toBeNull();

    counts.show();
    await loaded();
    expect(counts.needsPerson()).toBeNull();

    count.set(7);
    expect(counts.needsPerson()).toBe(7);
  });

  it('tells the reader the count is on screen, and when it no longer is', async () => {
    const counts = TestBed.inject(NavCounts);

    const release = counts.show();

    await loaded();
    expect(showing).toBe(1);

    release();
    expect(showing).toBe(0);
  });

  it('never registers a showing that was released before the reader arrived', async () => {
    const counts = TestBed.inject(NavCounts);

    counts.show()();
    // Long enough for the chunk to arrive; it must not register then.
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(showing).toBe(0);
  });
});
