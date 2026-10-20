import { HttpContext } from '@angular/common/http';
import { SESSION_PROBE, probeContext, readsAhead } from './session-probe';

describe('session probes', () => {
  const entry = ['sign-in', 'create-account', 'accept-invitation'];

  it('reads ahead on a page inside the workspace', () => {
    expect(readsAhead('/conversations/01a15013', entry)).toBe(true);
    expect(readsAhead('/overview', entry)).toBe(true);
    expect(readsAhead('/', entry)).toBe(true);
  });

  it('never on an entry page, where somebody has come to sign in', () => {
    // A browser whose session ended would only log the probes' 401s there
    // (Phase 36.11 matrix).
    expect(readsAhead('/sign-in', entry)).toBe(false);
    expect(readsAhead('/sign-in?reason=session-ended', entry)).toBe(false);
    expect(readsAhead('/create-account', entry)).toBe(false);
    expect(readsAhead('/accept-invitation#token', entry)).toBe(false);
  });

  it('marks a probe, and only a probe', () => {
    expect(probeContext(true).context?.get(SESSION_PROBE)).toBe(true);
    expect(probeContext(false).context).toBeUndefined();
    expect(new HttpContext().get(SESSION_PROBE)).toBe(false);
  });
});
