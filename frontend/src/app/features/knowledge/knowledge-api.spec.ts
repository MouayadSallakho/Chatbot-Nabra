import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { KnowledgeApi, KnowledgeDocument } from './knowledge-api';

/**
 * The knowledge client.
 *
 * What is worth asserting is the contract with the backend: the URLs, the query,
 * and above all that every mutation carries `If-Match` -- including indexing,
 * which is the one a client author would most plausibly assume is a read.
 */
describe('KnowledgeApi', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const documentId = '22222222-2222-2222-2222-222222222222';
  const agentId = '33333333-3333-3333-3333-333333333333';

  const knowledgeUrl = `/api/tenants/${tenantId}/knowledge`;
  const documentUrl = `${knowledgeUrl}/${documentId}`;
  const agentKnowledgeUrl = `/api/tenants/${tenantId}/ai-agents/${agentId}/knowledge`;

  const document: KnowledgeDocument = {
    id: documentId,
    tenantId,
    title: 'Returns policy',
    content: 'Goods may be returned within 30 days.',
    isEnabled: true,
    readiness: 'NeedsIndex',
    createdAt: '2026-09-01T09:00:00+00:00',
    updatedAt: '2026-09-01T09:00:00+00:00',
    indexedAt: null,
  };

  let api: KnowledgeApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), KnowledgeApi],
    });

    api = TestBed.inject(KnowledgeApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks the workspace-scoped list endpoint with explicit paging', () => {
    api.list(tenantId, 2, 50).subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === knowledgeUrl && candidate.method === 'GET',
    );

    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('50');

    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0 });
  });

  it('hands back the entity tag a read was taken at', () => {
    let etag: string | undefined;

    api.get(tenantId, documentId).subscribe((versioned) => (etag = versioned.etag));

    http.expectOne(documentUrl).flush(document, { headers: { ETag: '"v1"' } });

    expect(etag).toBe('"v1"');
  });

  it('creates without a precondition, because there is no previous version', () => {
    api.create(tenantId, { title: 'Returns policy', content: 'Text.' }).subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === knowledgeUrl && candidate.method === 'POST',
    );

    expect(request.request.headers.has('If-Match')).toBe(false);

    request.flush(document, { headers: { ETag: '"v1"' } });
  });

  it('sends the precondition when it replaces a document', () => {
    api
      .update(tenantId, documentId, { title: 'Returns policy', content: 'Changed.' }, '"v1"')
      .subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === documentUrl && candidate.method === 'PUT',
    );

    expect(request.request.headers.get('If-Match')).toBe('"v1"');

    request.flush(document, { headers: { ETag: '"v2"' } });
  });

  it('sends the precondition when it indexes', () => {
    // Indexing is a write: it replaces the chunk set and moves the version. A
    // client that sent it unconditionally would be refused with 428, and would
    // be a lost update waiting to happen if it ever were not.
    api.index(tenantId, documentId, '"v1"').subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === `${documentUrl}/index` && candidate.method === 'POST',
    );

    expect(request.request.headers.get('If-Match')).toBe('"v1"');

    request.flush(
      { document, chunkCount: 3, embeddingCalls: 1, alreadyCurrent: false },
      { headers: { ETag: '"v2"' } },
    );
  });

  it('sends the precondition when it enables or disables', () => {
    api.enable(tenantId, documentId, '"v1"').subscribe();

    const enable = http.expectOne(`${documentUrl}/enable`);
    expect(enable.request.headers.get('If-Match')).toBe('"v1"');
    enable.flush(document, { headers: { ETag: '"v2"' } });

    api.disable(tenantId, documentId, '"v2"').subscribe();

    const disable = http.expectOne(`${documentUrl}/disable`);
    expect(disable.request.headers.get('If-Match')).toBe('"v2"');
    disable.flush(document, { headers: { ETag: '"v3"' } });
  });

  it('reads an agent assignment from under the agent, with the agent version', () => {
    let etag: string | undefined;

    api.agentKnowledge(tenantId, agentId).subscribe((versioned) => (etag = versioned.etag));

    http
      .expectOne(agentKnowledgeUrl)
      .flush({ aiAgentId: agentId, documents: [] }, { headers: { ETag: '"agent-v1"' } });

    expect(etag).toBe('"agent-v1"');
  });

  it('replaces an assignment against the agent version', () => {
    api.replaceAgentKnowledge(tenantId, agentId, [documentId], '"agent-v1"').subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === agentKnowledgeUrl && candidate.method === 'PUT',
    );

    expect(request.request.headers.get('If-Match')).toBe('"agent-v1"');
    expect(request.request.body).toEqual({ knowledgeDocumentIds: [documentId] });

    request.flush({ aiAgentId: agentId, documents: [] }, { headers: { ETag: '"agent-v2"' } });
  });

  it('sends no precondition when it tests retrieval, because it changes nothing', () => {
    api.search(tenantId, agentId, 'Do you deliver?').subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === `${agentKnowledgeUrl}/search` && candidate.method === 'POST',
    );

    expect(request.request.headers.has('If-Match')).toBe(false);
    expect(request.request.body).toEqual({ question: 'Do you deliver?' });

    request.flush({ hits: [] });
  });

  it('fails loudly when a conditional resource comes back with no entity tag', () => {
    // Returning an empty string would mean sending it later as If-Match and
    // getting a confusing 412 nobody could explain.
    let failure: unknown;

    api.get(tenantId, documentId).subscribe({ error: (error) => (failure = error) });

    http.expectOne(documentUrl).flush(document);

    expect(failure).toBeInstanceOf(Error);
  });
});
