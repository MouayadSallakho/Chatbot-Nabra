/**
 * Clears the browser's `localStorage` and `sessionStorage` before and after
 * every test.
 *
 * The unit tests run without isolation, so spec files that share a worker
 * share one jsdom window, and its storage with it. A key left behind by one
 * file then shows up in whichever file that worker runs next, and the result
 * depends on how the files were split and ordered. (Phase 39.7a's checkpoint
 * failed this way: a conversation-context test remembered its workspace in the
 * real store, and the permission service's "writes nothing" test read it.)
 *
 * Specs should still give the code under test its own store
 * (`memoryStorage()` for `LOCAL_STORAGE`); this keeps the real ones empty
 * whatever a spec forgets. Clearing after each test as well means a test that
 * asserts on the real stores sees only what it wrote itself.
 *
 * Registered in `angular.json` (`test` → `options.setupFiles`).
 */
import { afterEach, beforeEach } from 'vitest';

function clearBrowserStorage(): void {
  localStorage.clear();
  sessionStorage.clear();
}

beforeEach(clearBrowserStorage);
afterEach(clearBrowserStorage);
