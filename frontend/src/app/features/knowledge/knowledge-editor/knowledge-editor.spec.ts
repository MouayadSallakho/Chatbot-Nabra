import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { KnowledgeApi, KnowledgeDocument } from '../knowledge-api';
import { KnowledgeEditor } from './knowledge-editor';

/**
 * The knowledge editor.
 *
 * The tests that matter most are the concurrency ones and the indexing ones.
 * "The request carried If-Match" says nothing about whether a reader's
 * half-written document survives a rejected save, and "indexing is explicit"
 * says nothing about whether a failed index leaves a document claiming to be
 * ready.
 */
describe('KnowledgeEditor', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const documentId = '22222222-2222-2222-2222-222222222222';
  const documentUrl = `/api/tenants/${tenantId}/knowledge/${documentId}`;

  const needsIndex: KnowledgeDocument = {
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

  const ready: KnowledgeDocument = {
    ...needsIndex,
    readiness: 'Ready',
    indexedAt: '2026-09-01T10:00:00+00:00',
  };

  let fixture: ComponentFixture<KnowledgeEditor>;
  let http: HttpTestingController;

  async function mount(document: KnowledgeDocument = needsIndex, etag = '"v1"'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [KnowledgeEditor],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
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

    fixture = TestBed.createComponent(KnowledgeEditor);
    fixture.componentRef.setInput('knowledgeDocumentId', documentId);
    fixture.detectChanges();

    http.expectOne(documentUrl).flush(document, { headers: { ETag: etag } });

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const query = <T extends HTMLElement>(selector: string): T | null =>
    host().querySelector<T>(selector);

  const contentBox = (): HTMLTextAreaElement =>
    query<HTMLTextAreaElement>('[data-testid="content"]')!;

  async function type(element: HTMLTextAreaElement, value: string): Promise<void> {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle();
  }

  function buttonLabelled(text: string): HTMLButtonElement | undefined {
    return [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  afterEach(() => http.verify());

  describe('loading', () => {
    it('shows the document and its state', async () => {
      await mount();

      expect(query('h1')?.textContent).toContain('Returns policy');
      expect(contentBox().value).toBe(needsIndex.content);
      expect(query('[data-testid="readiness"]')?.textContent).toContain('Needs index');
    });

    it('says plainly when a document is not answering anything yet', async () => {
      await mount();

      expect(host().textContent).toContain('not answering anything yet');
    });

    it('says plainly when a document is switched off', async () => {
      await mount({ ...ready, isEnabled: false, readiness: 'Disabled' });

      expect(host().textContent).toContain('switched off');
    });
  });

  describe('saving', () => {
    it('sends If-Match and adopts the new tag', async () => {
      await mount();

      await type(contentBox(), 'Goods may be returned within 14 days.');

      buttonLabelled('Save document')!.click();

      const save = http.expectOne(documentUrl);

      expect(save.request.method).toBe('PUT');
      expect(save.request.headers.get('If-Match')).toBe('"v1"');

      save.flush(
        { ...needsIndex, content: 'Goods may be returned within 14 days.' },
        { headers: { ETag: '"v2"' } },
      );

      await settle();

      // The new tag is in use.
      await type(contentBox(), 'Changed again.');
      buttonLabelled('Save document')!.click();

      const next = http.expectOne(documentUrl);

      expect(next.request.headers.get('If-Match')).toBe('"v2"');

      next.flush(needsIndex, { headers: { ETag: '"v3"' } });
      await settle();
    });

    it('sends nothing when nothing changed', async () => {
      // A save that rewrote the same values would still move the version, making
      // somebody else's open editor stale for no reason at all.
      await mount();

      const save = buttonLabelled('Save document');

      expect(save?.disabled).toBe(true);

      save?.click();
      await settle();

      http.verify();
    });

    it('warns that saved content will need indexing again', async () => {
      await mount(ready);

      await type(contentBox(), 'Something else entirely.');

      expect(host().textContent).toContain('needs indexing again');
    });
  });

  describe('indexing', () => {
    it('sends If-Match, because indexing is a write', async () => {
      await mount();

      query<HTMLButtonElement>('[data-testid="index-button"]')!.click();

      const index = http.expectOne(`${documentUrl}/index`);

      expect(index.request.method).toBe('POST');
      expect(index.request.headers.get('If-Match')).toBe('"v1"');

      index.flush(
        { document: ready, chunkCount: 3, embeddingCalls: 1, alreadyCurrent: false },
        { headers: { ETag: '"v2"' } },
      );

      await settle();

      expect(query('[data-testid="readiness"]')?.textContent).toContain('Ready');
      expect(query('[data-testid="index-result"]')?.textContent).toContain('3 pieces');
    });

    it('reports honestly when nothing needed doing', async () => {
      // The cost property somebody most needs to see for themselves.
      await mount(ready);

      query<HTMLButtonElement>('[data-testid="index-button"]')!.click();

      http
        .expectOne(`${documentUrl}/index`)
        .flush(
          { document: ready, chunkCount: 0, embeddingCalls: 0, alreadyCurrent: true },
          { headers: { ETag: '"v1"' } },
        );

      await settle();

      expect(query('[data-testid="index-result"]')?.textContent).toContain(
        'No request was sent to the AI provider',
      );
    });

    it('refuses to index unsaved text, without sending anything', async () => {
      // Indexing embeds what the server holds, so indexing while the editor holds
      // different text would produce an index of something nobody is looking at.
      await mount();

      await type(contentBox(), 'Not saved yet.');

      query<HTMLButtonElement>('[data-testid="index-button"]')!.click();
      await settle();

      expect(query('[data-testid="index-error"]')?.textContent).toContain('Save your changes');

      http.verify();
    });

    it('leaves the document unchanged when the provider is unavailable', async () => {
      // Exactly what a deployment with no credential does. The text stays, the
      // state stays, and nothing claims an index happened.
      await mount();

      query<HTMLButtonElement>('[data-testid="index-button"]')!.click();

      http
        .expectOne(`${documentUrl}/index`)
        .flush(
          { detail: 'No embedding provider is configured.' },
          { status: 503, statusText: 'Service Unavailable' },
        );

      await settle();

      expect(query('[data-testid="index-error"]')).not.toBeNull();
      expect(query('[data-testid="readiness"]')?.textContent).toContain('Needs index');
      expect(contentBox().value).toBe(needsIndex.content);
    });
  });

  describe('availability', () => {
    it('takes a document out of use with If-Match', async () => {
      await mount(ready);

      query<HTMLButtonElement>('[data-testid="disable-button"]')!.click();

      const disable = http.expectOne(`${documentUrl}/disable`);

      expect(disable.request.headers.get('If-Match')).toBe('"v1"');

      disable.flush(
        { ...ready, isEnabled: false, readiness: 'Disabled' },
        { headers: { ETag: '"v2"' } },
      );

      await settle();

      expect(query('[data-testid="readiness"]')?.textContent).toContain('Disabled');
      expect(query('[data-testid="enable-button"]')).not.toBeNull();
    });
  });

  describe('when a write is rejected as stale (412)', () => {
    /** Gets the editor into the conflicted state with unsaved text on screen. */
    async function conflict(): Promise<void> {
      await mount();

      await type(contentBox(), 'My unsaved work.');

      buttonLabelled('Save document')!.click();

      http
        .expectOne(documentUrl)
        .flush(
          { detail: 'Changed elsewhere.' },
          { status: 412, statusText: 'Precondition Failed' },
        );

      await settle();
    }

    it('keeps the unsaved text exactly as the reader left it', async () => {
      await conflict();

      expect(contentBox().value).toBe('My unsaved work.');
      expect(host().textContent).toContain('updated elsewhere');
      expect(host().textContent).toContain('Nothing you have typed has been lost');
    });

    it('retries nothing on its own', async () => {
      await conflict();

      // No second request went out, and the save control is blocked because the
      // tag was dropped.
      http.verify();

      expect(buttonLabelled('Save document')?.disabled).toBe(true);
    });

    it('reloads only when the reader accepts losing their work', async () => {
      await conflict();

      fixture.componentInstance['confirmReplace'] = () => false;

      buttonLabelled('Reload latest')!.click();
      await settle();

      // Declined: nothing was read, and the text is still theirs.
      http.verify();
      expect(contentBox().value).toBe('My unsaved work.');
    });

    it('performs a fresh read and captures the new tag when accepted', async () => {
      await conflict();

      fixture.componentInstance['confirmReplace'] = () => true;

      buttonLabelled('Reload latest')!.click();
      await settle();

      const reload = http.expectOne(documentUrl);

      expect(reload.request.method).toBe('GET');

      reload.flush(
        { ...needsIndex, content: "Somebody else's version." },
        { headers: { ETag: '"v9"' } },
      );

      await settle();

      expect(contentBox().value).toBe("Somebody else's version.");
      expect(host().textContent).not.toContain('updated elsewhere');

      // The new tag is in use, and the stale one is gone for good.
      await type(contentBox(), 'Now editing the latest.');
      buttonLabelled('Save document')!.click();

      const next = http.expectOne(documentUrl);

      expect(next.request.headers.get('If-Match')).toBe('"v9"');

      next.flush(needsIndex, { headers: { ETag: '"v10"' } });
      await settle();
    });

    it('treats a missing precondition (428) the same way, defensively', async () => {
      await mount();

      await type(contentBox(), 'My unsaved work.');

      buttonLabelled('Save document')!.click();

      http.expectOne(documentUrl).flush({}, { status: 428, statusText: 'Precondition Required' });

      await settle();

      expect(contentBox().value).toBe('My unsaved work.');
      expect(host().textContent).toContain('updated elsewhere');
    });
  });

  describe('untrusted content', () => {
    it('renders a document containing markup as text, never as markup', async () => {
      // A knowledge document is written by somebody in a workspace and is
      // untrusted. Anything that rendered it as markup would turn a document
      // into a script.
      const attack = '<script>alert(1)</script><img src=x onerror="alert(2)">';

      await mount({ ...needsIndex, title: attack, content: attack });

      expect(host().querySelector('script')).toBeNull();
      expect(host().querySelector('img')).toBeNull();
      expect(contentBox().value).toBe(attack);
      expect(query('h1')?.textContent).toContain(attack);
      expect(host().innerHTML).not.toContain('<script>alert(1)</script>');
    });
  });
});
