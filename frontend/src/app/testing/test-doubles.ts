import { MediaQueryFactory } from '@app/core/media/media';

/**
 * A media query the test controls.
 *
 * jsdom does not implement `window.matchMedia` at all, so there is nothing to
 * stub over -- and even in a real browser a test cannot change the operating
 * system's colour scheme or the window's width. Driving both through a double is
 * the only way to assert on theme resolution and on the responsive shell at all.
 */
class FakeMediaQueryList implements MediaQueryList {
  onchange: ((this: MediaQueryList, ev: MediaQueryListEvent) => unknown) | null = null;

  private readonly listeners = new Set<EventListenerOrEventListenerObject>();

  constructor(
    readonly media: string,
    private currentMatches: boolean,
  ) {}

  get matches(): boolean {
    return this.currentMatches;
  }

  /** Changes the result and notifies listeners, as a real query does. */
  set(matches: boolean): void {
    if (matches === this.currentMatches) {
      return;
    }

    this.currentMatches = matches;

    const event = { matches, media: this.media } as MediaQueryListEvent;

    for (const listener of this.listeners) {
      if (typeof listener === 'function') {
        listener.call(this, event);
      } else {
        listener.handleEvent(event);
      }
    }
  }

  // Signatures kept wide enough to satisfy the DOM interface, which allows both
  // a handler function and an object with `handleEvent`.
  addEventListener(_type: string, listener: EventListenerOrEventListenerObject | null): void {
    if (listener) {
      this.listeners.add(listener);
    }
  }

  removeEventListener(_type: string, listener: EventListenerOrEventListenerObject | null): void {
    if (listener) {
      this.listeners.delete(listener);
    }
  }

  /** Deprecated DOM surface, present only to satisfy the interface. */
  addListener(): void {}
  removeListener(): void {}
  dispatchEvent(): boolean {
    return false;
  }
}

/** Serves fake media queries and lets a test change any of them. */
export class FakeMediaMatcher {
  private readonly lists = new Map<string, FakeMediaQueryList>();

  constructor(private readonly initial: Readonly<Record<string, boolean>> = {}) {}

  /** Supply this in place of MEDIA_QUERY_FACTORY. */
  readonly factory: MediaQueryFactory = (query) => {
    let list = this.lists.get(query);

    if (!list) {
      list = new FakeMediaQueryList(query, this.initial[query] ?? false);
      this.lists.set(query, list);
    }

    return list;
  };

  /** Simulates the environment changing: a theme switch, or a resize. */
  set(query: string, matches: boolean): void {
    this.factory(query);
    this.lists.get(query)?.set(matches);
  }
}

/**
 * An in-memory `Storage`.
 *
 * Tests must not share the real `localStorage`: it would leak state between
 * them and make results depend on execution order. It also lets a test seed a
 * persisted value without touching the browser.
 */
export function memoryStorage(seed: Readonly<Record<string, string>> = {}): Storage {
  const entries = new Map<string, string>(Object.entries(seed));

  return {
    get length() {
      return entries.size;
    },
    clear: () => entries.clear(),
    getItem: (key) => entries.get(key) ?? null,
    key: (index) => [...entries.keys()][index] ?? null,
    removeItem: (key) => {
      entries.delete(key);
    },
    setItem: (key, value) => {
      entries.set(key, value);
    },
  };
}

/** A store that refuses every operation, as a full or blocked one does. */
export function hostileStorage(): Storage {
  const refuse = (): never => {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  };

  return {
    length: 0,
    clear: refuse,
    getItem: refuse,
    key: refuse,
    removeItem: refuse,
    setItem: refuse,
  };
}
