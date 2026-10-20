import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Marks a request sent before the session is confirmed (Phase 36.11).
 *
 * At startup the workspace list and its permissions are read alongside
 * `/api/auth/me` instead of after it, when this browser already holds a
 * workspace selection. If the session turns out to have ended, those reads
 * answer 401 too -- and that is not news: `/me` already says so, and the
 * sign-in screen follows from it. The session interceptor therefore leaves a
 * probe's 401 alone: no sign-out, no cleared selection, no redirect.
 */
export const SESSION_PROBE = new HttpContextToken<boolean>(() => false);

/**
 * Whether startup may read ahead on this path (Phase 36.11). Not on the entry
 * pages: somebody there has come to sign in, so a browser whose session ended
 * would only log the probes' refusals beside `/me`'s.
 */
export function readsAhead(path: string, entryPaths: readonly string[]): boolean {
  const first = path.split(/[?#]/)[0].split('/').filter(Boolean)[0] ?? '';

  return !entryPaths.includes(first);
}

/** The request options for a probe, or none for an ordinary read. */
export function probeContext(probe: boolean): { context?: HttpContext } {
  return probe ? { context: new HttpContext().set(SESSION_PROBE, true) } : {};
}
