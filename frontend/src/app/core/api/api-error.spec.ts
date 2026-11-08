import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ApiErrorKind, toApiError } from './api-error';

/** Builds a Problem Details failure the way the backend sends one. */
function problem(
  status: number,
  detail?: string,
  url = '/api/tenants/x/ai-agents',
): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    statusText: 'error',
    url,
    error: detail === undefined ? null : { type: 'about:blank', title: 'A title', detail },
  });
}

describe('toApiError', () => {
  const mappings: readonly (readonly [number, ApiErrorKind])[] = [
    [400, 'validation'],
    [404, 'notFound'],
    [409, 'conflict'],
    [412, 'stale'],
    [428, 'preconditionRequired'],
    [502, 'invalidResponse'],
    [503, 'providerUnavailable'],
    [504, 'providerTimeout'],
    [500, 'unknown'],
  ];

  for (const [status, kind] of mappings) {
    it(`reads ${status} as ${kind}`, () => {
      expect(toApiError(problem(status)).kind).toBe(kind);
    });
  }

  it('treats an unanswered request as offline rather than as a server failure', () => {
    // Status 0 means the request never reached anything. The advice is entirely
    // different from any server-side failure, so it is its own kind.
    const error = toApiError(problem(0));

    expect(error.kind).toBe('offline');
    expect(error.message).toContain('could not be reached');
  });

  it('keeps the status for logging and for assertions', () => {
    expect(toApiError(problem(412)).status).toBe(412);
  });

  describe('what reaches the reader', () => {
    it('explains a stale antiforgery token as nothing the reader did wrong', () => {
      const error = toApiError(
        new HttpErrorResponse({
          status: 400,
          error: {
            status: 400,
            code: 'antiforgery.invalid',
            detail: 'Reload the page and try again.',
          },
        }),
      );

      // The status keeps its meaning; only the sentence changes, and it does not
      // send the reader to check fields that are fine.
      expect(error.kind).toBe('validation');
      expect(error.code).toBe('antiforgery.invalid');
      expect(error.message).toContain('Nothing was changed');
      expect(error.message).not.toContain('highlighted fields');
    });

    it('uses the backend wording where it is written for a reader', () => {
      const error = toApiError(problem(400, 'Name must be 120 characters or fewer.'));

      expect(error.message).toBe('Name must be 120 characters or fewer.');
    });

    it('uses the backend wording for a lifecycle refusal', () => {
      const error = toApiError(problem(409, 'A draft agent cannot be disabled.'));

      expect(error.message).toBe('A draft agent cannot be disabled.');
    });

    it('never shows a server fault detail, even when one is present', () => {
      // A 500 has nothing a reader can act on, and its details belong in a log.
      // This is the guard against a careless future handler putting internals in
      // a body that the client then renders.
      const error = toApiError(problem(500, 'Npgsql.PostgresException: relation does not exist'));

      expect(error.message).not.toContain('Npgsql');
      expect(error.message).not.toContain('PostgresException');
    });

    it('never shows provider internals for an upstream failure', () => {
      const error = toApiError(
        problem(503, 'OpenAI key rejected for org-1234', '/api/tenants/x/ai-agents/y/preview'),
      );

      expect(error.message).not.toContain('OpenAI');
      expect(error.message).not.toContain('org-1234');
      // The reader still learns the useful part -- that this deployment has no
      // provider -- without a word of what the provider said.
      expect(error.message).toContain('none is configured on this deployment');
    });

    it('does not blame an AI provider for a 503 on an ordinary read', () => {
      const error = toApiError(problem(503, undefined, '/api/tenants/x/knowledge?page=1'));

      expect(error.kind).toBe('providerUnavailable');
      expect(error.message).toContain('temporarily unavailable');
      expect(error.message).not.toContain('AI provider');
    });

    it('explains a timeout without inviting an automatic retry', () => {
      expect(toApiError(problem(504)).message).toContain('too long');
    });

    it('falls back to its own copy when the body has no usable detail', () => {
      expect(toApiError(problem(400)).message).toContain('rejected');
      expect(toApiError(problem(400, '   ')).message).toContain('rejected');
    });

    it('ignores a body that is not Problem Details', () => {
      const failure = new HttpErrorResponse({ status: 400, error: '<html>gateway</html>' });

      expect(toApiError(failure).message).not.toContain('html');
    });

    it('never surfaces the rest of the Problem Details payload', () => {
      const failure = new HttpErrorResponse({
        status: 404,
        error: {
          type: 'https://tools.ietf.org/html/rfc9110',
          title: 'AI agent not found',
          detail: 'This tenant has no AI agent with that id.',
          traceId: '00-abc123-def456-01',
        },
      });

      const error = toApiError(failure);

      expect(error.message).toBe('This tenant has no AI agent with that id.');
      expect(error.message).not.toContain('traceId');
      expect(error.message).not.toContain('00-abc123');
    });
  });

  describe('rate limiting', () => {
    /** A 429 as the API sends one, with whatever Retry-After the case needs. */
    function refused(retryAfter?: string): HttpErrorResponse {
      return new HttpErrorResponse({
        status: 429,
        statusText: 'Too Many Requests',
        error: { title: 'Too many requests', detail: 'Too many requests were made.' },
        headers:
          retryAfter === undefined
            ? new HttpHeaders()
            : new HttpHeaders({ 'Retry-After': retryAfter }),
      });
    }

    it('is its own kind rather than a conflict', () => {
      // The advice is unique: nothing is wrong with the request and nothing needs
      // correcting -- the caller simply has to wait. Folding it into 'conflict'
      // would have every screen telling people to change something.
      expect(toApiError(refused('30')).kind).toBe('rateLimited');
    });

    it('reads a Retry-After given in seconds', () => {
      expect(toApiError(refused('45')).retryAfterSeconds).toBe(45);
    });

    it('leaves the wait undefined when the header is absent', () => {
      expect(toApiError(refused()).retryAfterSeconds).toBeUndefined();
    });

    it('refuses an HTTP-date Retry-After rather than guessing', () => {
      // Parsing a date would mean trusting the reader's clock against the
      // server's, and this platform only ever sends the numeric form.
      expect(
        toApiError(refused('Wed, 21 Oct 2026 07:28:00 GMT')).retryAfterSeconds,
      ).toBeUndefined();
    });

    it.each(['not-a-number', '', '  ', '1.5', '-5', 'Infinity', 'NaN'])(
      'ignores a Retry-After of %o',
      (header) => {
        // Dropped rather than coerced, so a screen either has a real number or has
        // none. A NaN reaching the copy would render as "try again in NaN seconds".
        expect(toApiError(refused(header)).retryAfterSeconds).toBeUndefined();
      },
    );

    it('caps an absurd wait at an hour', () => {
      // Anything longer is a misconfiguration or a server saying "come back
      // tomorrow", and neither is worth rendering as a countdown next to a message
      // somebody is waiting to send.
      expect(toApiError(refused('999999')).retryAfterSeconds).toBe(3600);
    });

    it('accepts a wait of zero without turning it into a countdown', () => {
      expect(toApiError(refused('0')).retryAfterSeconds).toBe(0);
    });

    it('uses our own copy rather than the server detail', () => {
      // 429 is deliberately absent from the statuses whose detail is shown. The
      // server's sentence is safe, but the policy is not the reader's business and
      // this keeps a future handler from becoming careless with it.
      const error = toApiError(refused('30'));

      expect(error.message).toContain('Too many requests were made just now');
      expect(error.message).not.toContain('Too many requests were made.');
    });

    it('carries no wait on any other status', () => {
      // Only a 429 has a Retry-After worth reading. A stray header on a 500 must
      // not turn into advice.
      const failure = new HttpErrorResponse({
        status: 500,
        headers: new HttpHeaders({ 'Retry-After': '30' }),
      });

      // The header is read uniformly, which is simpler than special-casing -- but
      // no screen consults it outside the rate-limited kind.
      expect(toApiError(failure).kind).toBe('unknown');
    });
  });

  describe('worded by code (Phase 38.1a)', () => {
    /** A refusal carrying a code, as every 400, 409, 412 and 422 now does. */
    function coded(
      status: number,
      body: object,
      headers?: Record<string, string>,
    ): HttpErrorResponse {
      return new HttpErrorResponse({
        status,
        url: '/api/auth/login',
        error: { type: 'about:blank', title: 'A title', ...body },
        headers: new HttpHeaders(headers),
      });
    }

    afterEach(() => {
      document.documentElement.lang = '';
    });

    it('takes the words from the catalogue rather than from the server', () => {
      const error = toApiError(
        coded(401, { code: 'auth.credentials.invalid', detail: 'Some English from the server.' }),
      );

      expect(error.message).toBe('The email address or password is incorrect.');
    });

    it('reads every rule the server named, and only strings', () => {
      const error = toApiError(
        coded(400, {
          code: 'auth.password.too_short',
          codes: ['auth.password.too_short', 'auth.password.requires_digit', 7, null],
        }),
      );

      expect(error.codes).toEqual(['auth.password.too_short', 'auth.password.requires_digit']);
    });

    it('carries the bound a value broke', () => {
      expect(toApiError(coded(422, { code: 'x', limit: 64 })).limit).toBe(64);
    });

    it.each([['64'], [-1], [Number.NaN], [null]])('refuses a limit of %o', (limit) => {
      expect(toApiError(coded(422, { code: 'x', limit })).limit).toBeUndefined();
    });

    it('puts Retry-After into the words', () => {
      const error = toApiError(coded(429, { code: 'auth.throttled' }, { 'Retry-After': '900' }));

      expect(error.retryAfterSeconds).toBe(900);
      expect(error.message).toBe('Too many attempts. Try again in 15 minutes.');
    });

    it('says signing in is unavailable on auth.throttle.unavailable', () => {
      const error = toApiError(coded(503, { code: 'auth.throttle.unavailable' }));

      expect(error.message).toContain('Signing in is unavailable for a moment');
    });

    it('takes the field names of an errors map, and never its messages', () => {
      const error = toApiError(
        coded(400, {
          code: 'validation.failed',
          errors: { Email: ['The English the reader must never see.'], password: ['More.'] },
        }),
      );

      expect(error.fields).toEqual(['Email', 'password']);
      expect(JSON.stringify(error)).not.toContain('never see');
    });

    it('takes no fields from a body without an errors map', () => {
      expect(toApiError(coded(400, { code: 'x' })).fields).toBeUndefined();
    });

    it('keeps the generic words for a code nobody registered, in English keeping the detail', () => {
      // The screens not yet moved say what they said before, until 38.5.
      expect(toApiError(coded(409, { code: 'knowledge.x', detail: 'Server words.' })).message).toBe(
        'Server words.',
      );
    });

    describe('on an Arabic page', () => {
      beforeEach(() => {
        document.documentElement.lang = 'ar';
      });

      it.each([
        [400, 'Some of the details were rejected'],
        [404, 'no longer exists'],
        [409, 'not available for this item'],
        [410, 'no longer available'],
        [413, 'too large'],
        [415, 'not one of the formats'],
        [422, 'No usable text'],
      ])('never shows the server detail on a %i', (status, generic) => {
        const error = toApiError(
          coded(status, { code: 'area.unknown', detail: 'Server English.' }),
        );

        expect(error.message).not.toContain('Server English');
        expect(error.message).toContain(generic);
      });

      it('never shows the detail of a refusal with no code either', () => {
        const error = toApiError(problem(409, 'Server English.'));

        expect(error.message).not.toContain('Server English');
      });

      it('still words a known code', () => {
        const error = toApiError(
          coded(401, { code: 'auth.credentials.invalid', detail: 'Server English.' }),
        );

        expect(error.message).toBe('The email address or password is incorrect.');
      });
    });
  });

  it('survives something that is not an HTTP failure at all', () => {
    expect(toApiError(new Error('boom')).kind).toBe('unknown');
    expect(toApiError(undefined).kind).toBe('unknown');
  });
});
