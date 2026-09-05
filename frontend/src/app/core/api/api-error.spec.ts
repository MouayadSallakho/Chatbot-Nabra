import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorKind, toApiError } from './api-error';

/** Builds a Problem Details failure the way the backend sends one. */
function problem(status: number, detail?: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    statusText: 'error',
    url: '/api/tenants/x/ai-agents',
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
    expect(error.message).toContain('backend');
  });

  it('keeps the status for logging and for assertions', () => {
    expect(toApiError(problem(412)).status).toBe(412);
  });

  describe('what reaches the reader', () => {
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
      const error = toApiError(problem(503, 'OpenAI key rejected for org-1234'));

      expect(error.message).not.toContain('OpenAI');
      expect(error.message).not.toContain('org-1234');
      expect(error.message).toContain('no provider is configured');
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

  it('survives something that is not an HTTP failure at all', () => {
    expect(toApiError(new Error('boom')).kind).toBe('unknown');
    expect(toApiError(undefined).kind).toBe('unknown');
  });
});
