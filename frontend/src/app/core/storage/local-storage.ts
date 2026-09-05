import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/**
 * The browser's `localStorage`, or `null` where it cannot be used.
 *
 * Behind a token for two reasons. Tests need to supply their own store so they
 * do not share state through the real one and pass or fail depending on
 * execution order. And `localStorage` does not merely return null when it is
 * unavailable -- accessing the property itself throws a `SecurityError` in
 * private modes, for opaque origins and under some enterprise policies.
 * Resolving to `null` once, here, means every caller can treat "no storage" as
 * an ordinary case rather than wrapping its own access in try/catch.
 *
 * Callers must still guard their reads and writes: a store that exists can
 * refuse a write because it is full.
 */
export const LOCAL_STORAGE = new InjectionToken<Storage | null>('LOCAL_STORAGE', {
  providedIn: 'root',
  factory: () => {
    try {
      return inject(DOCUMENT).defaultView?.localStorage ?? null;
    } catch {
      return null;
    }
  },
});

/** Reads a key, returning null for a missing value or an unusable store. */
export function readStored(storage: Storage | null, key: string): string | null {
  try {
    return storage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

/** Writes a key, ignoring a store that refuses. */
export function writeStored(storage: Storage | null, key: string, value: string): void {
  try {
    storage?.setItem(key, value);
  } catch {
    // A full or unavailable store must never break an interaction. The choice
    // still applies for this session; it simply will not survive a reload.
  }
}
