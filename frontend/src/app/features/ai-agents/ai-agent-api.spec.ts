import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { AiAgent, AiAgentApi } from './ai-agent-api';

/**
 * The AI agent client.
 *
 * What is worth asserting here is the contract with the backend: the URLs, the
 * query, and above all that every mutation carries `If-Match` and that the new
 * entity tag comes back out. Angular's own request plumbing is not under test.
 */
describe('AiAgentApi', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentId = '22222222-2222-2222-2222-222222222222';
  const agentsUrl = `/api/tenants/${tenantId}/ai-agents`;
  const agentUrl = `${agentsUrl}/${agentId}`;

  const agent: AiAgent = {
    id: agentId,
    tenantId,
    name: 'Sales Assistant',
    instructions: 'Help customers choose suitable products.',
    status: 'Draft',
    createdAt: '2026-05-01T09:00:00+00:00',
    updatedAt: '2026-05-01T09:00:00+00:00',
  };

  let api: AiAgentApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), AiAgentApi],
    });

    api = TestBed.inject(AiAgentApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('listing', () => {
    it('asks the tenant-scoped list endpoint with explicit paging', () => {
      api.list(tenantId, 2, 50).subscribe();

      const request = http.expectOne(
        (candidate) => candidate.url === agentsUrl && candidate.method === 'GET',
      );

      expect(request.request.params.get('page')).toBe('2');
      expect(request.request.params.get('pageSize')).toBe('50');

      request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0 });
    });

    it('defaults to the first page', () => {
      api.list(tenantId).subscribe();

      const request = http.expectOne((candidate) => candidate.url === agentsUrl);

      expect(request.request.params.get('page')).toBe('1');
      expect(request.request.params.get('pageSize')).toBe('20');

      request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });

    it('sends no precondition, because a list is not written to', () => {
      api.list(tenantId).subscribe();

      const request = http.expectOne((candidate) => candidate.url === agentsUrl);

      expect(request.request.headers.has('If-Match')).toBe(false);

      request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    });
  });

  describe('reading one agent', () => {
    it('captures the entity tag beside the body', async () => {
      const result = api.get(tenantId, agentId);
      const pending = new Promise((resolve) => result.subscribe(resolve));

      http.expectOne(agentUrl).flush(agent, { headers: { ETag: '"v1"' } });

      expect(await pending).toEqual({ agent, etag: '"v1"' });
    });

    it('fails loudly when the response carries no entity tag', async () => {
      // A missing tag is a broken contract. Returning an empty string would let
      // it be sent later as If-Match and rejected with a confusing 412.
      let caught: unknown;
      const pending = new Promise<void>((resolve) =>
        api.get(tenantId, agentId).subscribe({
          error: (error) => {
            caught = error;
            resolve();
          },
        }),
      );

      http.expectOne(agentUrl).flush(agent);
      await pending;

      expect(caught).toBeInstanceOf(Error);
    });
  });

  describe('creating', () => {
    it('posts to the collection and returns the new tag', async () => {
      const pending = new Promise((resolve) =>
        api.create(tenantId, { name: 'Sales', instructions: 'Be helpful.' }).subscribe(resolve),
      );

      const request = http.expectOne(
        (candidate) => candidate.url === agentsUrl && candidate.method === 'POST',
      );

      expect(request.request.body).toEqual({ name: 'Sales', instructions: 'Be helpful.' });
      // Nothing the backend owns is ever sent.
      expect(Object.keys(request.request.body as object)).toEqual(['name', 'instructions']);
      // No precondition: there is no previous version of something new.
      expect(request.request.headers.has('If-Match')).toBe(false);

      request.flush(agent, { headers: { ETag: '"v1"' } });

      expect(await pending).toEqual({ agent, etag: '"v1"' });
    });
  });

  describe('mutations', () => {
    const cases = [
      {
        what: 'rename',
        url: `${agentUrl}/name`,
        method: 'PUT',
        body: { name: 'Renamed' },
        call: () => api.rename(tenantId, agentId, 'Renamed', '"v1"'),
      },
      {
        what: 'update instructions',
        url: `${agentUrl}/instructions`,
        method: 'PUT',
        body: { instructions: 'New instructions.' },
        call: () => api.updateInstructions(tenantId, agentId, 'New instructions.', '"v1"'),
      },
      {
        what: 'activate',
        url: `${agentUrl}/activate`,
        method: 'POST',
        body: null,
        call: () => api.activate(tenantId, agentId, '"v1"'),
      },
      {
        what: 'disable',
        url: `${agentUrl}/disable`,
        method: 'POST',
        body: null,
        call: () => api.disable(tenantId, agentId, '"v1"'),
      },
    ];

    for (const testCase of cases) {
      it(`sends If-Match when it calls ${testCase.what}`, () => {
        testCase.call().subscribe();

        const request = http.expectOne(testCase.url);

        expect(request.request.method).toBe(testCase.method);
        expect(request.request.headers.get('If-Match')).toBe('"v1"');
        expect(request.request.body).toEqual(testCase.body);

        request.flush(agent, { headers: { ETag: '"v2"' } });
      });

      it(`returns the advanced tag from ${testCase.what}`, async () => {
        const pending = new Promise((resolve) => testCase.call().subscribe(resolve));

        http.expectOne(testCase.url).flush(agent, { headers: { ETag: '"v2"' } });

        // The new tag, not the one that was sent. A caller that kept using the
        // old one would be refused on its very next save.
        expect(await pending).toEqual({ agent, etag: '"v2"' });
      });
    }

    it('passes the tag through opaquely, without parsing or reshaping it', () => {
      const strange = 'W/"not-a-number-\\"quoted\\""';

      api.rename(tenantId, agentId, 'Renamed', strange).subscribe();

      const request = http.expectOne(`${agentUrl}/name`);

      expect(request.request.headers.get('If-Match')).toBe(strange);

      request.flush(agent, { headers: { ETag: '"v2"' } });
    });
  });

  describe('preview', () => {
    it('posts the message and sends no precondition', async () => {
      const pending = new Promise((resolve) =>
        api.preview(tenantId, agentId, 'Which laptop?').subscribe(resolve),
      );

      const request = http.expectOne(`${agentUrl}/preview`);

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ message: 'Which laptop?' });
      // Preview changes nothing, so there is no write to protect.
      expect(request.request.headers.has('If-Match')).toBe(false);

      request.flush({ reply: 'The 14-inch model.' });

      expect(await pending).toEqual({ reply: 'The 14-inch model.' });
    });

    it('ignores any entity tag a preview response happens to carry', async () => {
      // The result type has no tag at all, so a preview can never advance the
      // version the editor is holding for its saves.
      const pending = new Promise((resolve) =>
        api.preview(tenantId, agentId, 'Hello').subscribe(resolve),
      );

      http.expectOne(`${agentUrl}/preview`).flush({ reply: 'Hi.' }, { headers: { ETag: '"v9"' } });

      expect(await pending).toEqual({ reply: 'Hi.' });
    });
  });

  it('never names a host, so the same build works behind any proxy', () => {
    api.list(tenantId).subscribe();

    const request = http.expectOne((candidate) => candidate.url.startsWith('/api/'));

    expect(request.request.url).not.toContain('http://');
    expect(request.request.url).not.toContain('localhost');

    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });
});
