import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { KnowledgeApi, KnowledgeDocumentSummary } from '../knowledge-api';
import { KnowledgeList } from './knowledge-list';

/**
 * The knowledge list.
 *
 * The states are what matter: loading, empty, refused and error each need to say
 * something different, and the readiness column has to be honest about documents
 * that cannot answer anything yet.
 */
describe('KnowledgeList', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const knowledgeUrl = `/api/tenants/${tenantId}/knowledge`;

  const ready: KnowledgeDocumentSummary = {
    id: '22222222-2222-2222-2222-222222222222',
    tenantId,
    title: 'Returns policy',
    isEnabled: true,
    readiness: 'Ready',
    contentLength: 240,
    createdAt: '2026-09-01T09:00:00+00:00',
    updatedAt: '2026-09-01T09:00:00+00:00',
    indexedAt: '2026-09-01T10:00:00+00:00',
    sourceFileType: null,
  };

  const unindexed: KnowledgeDocumentSummary = {
    ...ready,
    id: '33333333-3333-3333-3333-333333333333',
    title: 'Delivery times',
    readiness: 'NeedsIndex',
    indexedAt: null,
  };

  let fixture: ComponentFixture<KnowledgeList>;
  let http: HttpTestingController;

  /** Mounts the list. Pass `permissions` to model a restricted reader. */
  async function mount(permissions: readonly string[] = ['*']): Promise<void> {
    TestBed.configureTestingModule({
      imports: [KnowledgeList],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        KnowledgeApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(KnowledgeList);
    fixture.detectChanges();

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  function flush(items: readonly KnowledgeDocumentSummary[], totalCount = items.length): void {
    http
      .expectOne((request) => request.url === knowledgeUrl)
      .flush({
        items,
        page: 1,
        pageSize: 20,
        totalCount,
      });
  }

  afterEach(() => http.verify());

  it('lists the workspace documents with their real state', async () => {
    await mount();

    flush([ready, unindexed]);
    await settle();

    expect(host().textContent).toContain('Returns policy');
    expect(host().textContent).toContain('Delivery times');

    // Exactly the API's states, each with its own badge.
    const badges = [...host().querySelectorAll('.knowledge-state .badge')];

    expect(badges.map((badge) => badge.textContent?.trim())).toEqual(['Indexed', 'Needs index']);
    expect(badges[0].classList).toContain('badge-success');
    expect(badges[1].classList).toContain('badge-warning');
  });

  it("says each document's size in characters, with the right plural", async () => {
    await mount();

    flush([ready, { ...unindexed, contentLength: 1 }]);
    await settle();

    expect(host().textContent).toContain('240 characters');
    expect(host().textContent).toContain('1 character');
  });

  it('keeps a title in its own direction', async () => {
    await mount();

    flush([{ ...ready, title: 'سياسة الإرجاع' }]);
    await settle();

    expect(host().querySelector('.knowledge-title')?.getAttribute('dir')).toBe('auto');
  });

  it('tells a written document apart from an uploaded one', async () => {
    await mount();

    flush([ready, { ...unindexed, sourceFileType: 'Pdf' }]);
    await settle();

    // Provenance, in one word per row: somebody looking for "the policy we
    // uploaded" should not have to open every document to find it.
    expect(host().textContent).toContain('Written');
    expect(host().textContent).toContain('PDF');
  });

  it('never puts a source filename in the list', async () => {
    await mount();

    flush([{ ...unindexed, sourceFileType: 'Docx' }]);
    await settle();

    // Only the format. A filename can itself say something about a business,
    // and a list of two hundred rows is not where it needs reading.
    expect(host().textContent).toContain('Word');
    expect(host().textContent).not.toContain('.docx');
  });

  it('dates the index under the state only when there is one', async () => {
    await mount();

    flush([ready, unindexed]);
    await settle();

    const states = [...host().querySelectorAll('.knowledge-state')];

    expect(states[0].querySelector('time')?.getAttribute('datetime')).toBe(ready.indexedAt);
    expect(states[1].querySelector('time')).toBeNull();
  });

  it('explains an empty workspace rather than saying "no data"', async () => {
    // Somebody reaching an empty list for the first time needs to know what the
    // thing is before they can want one.
    await mount();

    flush([]);
    await settle();

    expect(host().textContent).toContain('No knowledge yet');
    expect(host().textContent).toContain('Write your first document');
  });

  it('offers no write action to somebody who may only read', async () => {
    await mount(['knowledge.view']);

    flush([]);
    await settle();

    expect(host().textContent).toContain('No knowledge yet');
    expect(host().textContent).not.toContain('Write your first document');
  });

  it('refuses without asking, and without naming a permission code', async () => {
    // Nothing worth requesting. The screen explains the refusal rather than
    // reporting an error from a request that was certain to be refused.
    await mount(['workspace.view']);

    http.verify();

    expect(host().textContent).toContain('do not have access to knowledge');
    expect(host().textContent).not.toContain('knowledge.view');
    expect(host().textContent).not.toContain('403');
  });

  it('offers a retry when the list could not be read', async () => {
    await mount();

    http
      .expectOne((request) => request.url === knowledgeUrl)
      .flush({}, { status: 500, statusText: 'Server Error' });

    await settle();

    expect(host().textContent).toContain('Could not load knowledge');
    expect(host().textContent).toContain('Try again');
  });

  it('renders a document title containing markup as text', async () => {
    // Titles are tenant-authored and untrusted, exactly like the content.
    await mount();

    flush([{ ...ready, title: '<img src=x onerror="alert(1)">' }]);
    await settle();

    expect(host().querySelector('img')).toBeNull();
    expect(host().textContent).toContain('<img src=x onerror="alert(1)">');
  });
});
