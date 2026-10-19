import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { AgentKnowledgeOption, KnowledgeApi } from '../knowledge-api';
import { AgentKnowledgeSection } from './agent-knowledge';

/**
 * The knowledge section of the agent editor.
 *
 * Two things are worth proving here and nowhere else: that a refused save leaves
 * the reader's ticks exactly where they put them, and that a document's real
 * state is shown rather than a flattering one.
 */
describe('AgentKnowledgeSection', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentId = '22222222-2222-2222-2222-222222222222';
  const knowledgeUrl = `/api/tenants/${tenantId}/ai-agents/${agentId}/knowledge`;

  const readyDocument: AgentKnowledgeOption = {
    knowledgeDocumentId: '33333333-3333-3333-3333-333333333333',
    title: 'Returns policy',
    isAssigned: false,
    readiness: 'Ready',
  };

  const unindexed: AgentKnowledgeOption = {
    knowledgeDocumentId: '44444444-4444-4444-4444-444444444444',
    title: 'Delivery times',
    isAssigned: false,
    readiness: 'NeedsIndex',
  };

  let fixture: ComponentFixture<AgentKnowledgeSection>;
  let http: HttpTestingController;

  async function mount(
    documents: readonly AgentKnowledgeOption[] = [readyDocument, unindexed],
    etag: string | null = '"agent-v1"',
    permissions: readonly string[] = ['*'],
  ): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AgentKnowledgeSection],
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

    fixture = TestBed.createComponent(AgentKnowledgeSection);
    fixture.componentRef.setInput('tenantId', tenantId);
    fixture.componentRef.setInput('aiAgentId', agentId);
    fixture.componentRef.setInput('agentETag', etag);
    fixture.detectChanges();

    http
      .expectOne(knowledgeUrl)
      .flush({ aiAgentId: agentId, documents }, { headers: { ETag: '"agent-v1"' } });

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

  /** The titles in one of the two lists, in order. */
  const titles = (list: 'selected' | 'available'): string[] =>
    [...host().querySelectorAll(`[data-testid="assignment-${list}"] .chooser-title`)].map(
      (title) => title.textContent?.trim() ?? '',
    );

  /** The Add or Remove button for one document. */
  const moveButton = (option: AgentKnowledgeOption): HTMLButtonElement | null =>
    query<HTMLButtonElement>(`[data-move="${option.knowledgeDocumentId}"]`);

  /** Moves a document to the other list, the way a reader would. */
  async function move(option: AgentKnowledgeOption): Promise<void> {
    moveButton(option)!.click();
    await settle();
  }

  const saveButton = (): HTMLButtonElement =>
    query<HTMLButtonElement>('[data-testid="save-assignment"]')!;

  afterEach(() => http.verify());

  describe('reading', () => {
    it('offers every document in the workspace with nothing assigned by default', async () => {
      // Knowledge is not shared automatically. An agent begins with nothing.
      await mount();

      expect(titles('available')).toEqual(['Returns policy', 'Delivery times']);
      expect(titles('selected')).toEqual([]);
      expect(host().textContent).toContain('Nothing selected');
    });

    it('shows each document real state, never a flattering one', async () => {
      // Showing an unindexed document as usable would leave somebody believing
      // their agent knows something it does not.
      await mount();

      expect(host().textContent).toContain('Indexed');
      expect(host().textContent).toContain('Needs index');
    });

    it('says how many assigned documents can actually answer today', async () => {
      await mount([
        { ...readyDocument, isAssigned: true },
        { ...unindexed, isAssigned: true },
      ]);

      expect(query('[data-testid="assignment-summary"]')?.textContent).toContain('Selected: 2');
      expect(query('[data-testid="assignment-summary"]')?.textContent).toContain(
        'Ready to answer from: 1',
      );
      expect(host().textContent).toContain('not used yet');
    });

    it('points somewhere useful when the workspace has no documents', async () => {
      await mount([]);

      expect(host().textContent).toContain('no knowledge documents yet');
    });
  });

  describe('moving between the lists', () => {
    it('names each button after its document, starting with the visible word', async () => {
      await mount();

      expect(moveButton(readyDocument)?.getAttribute('aria-label')).toBe('Add “Returns policy”');
    });

    it('keeps the reader on the list they were working through, and says what moved', async () => {
      await mount();

      moveButton(readyDocument)!.focus();
      await move(readyDocument);

      expect(titles('selected')).toEqual(['Returns policy']);
      // Focus lands on the document that took its place in the list it left.
      expect(document.activeElement).toBe(moveButton(unindexed));
      expect(query('[data-testid="assignment-announcement"]')?.textContent).toContain(
        'Added “Returns policy” to this agent. Not saved yet.',
      );
      expect(query('[data-testid="assignment-unsaved"]')).not.toBeNull();
    });

    it('moves focus to the list heading when the list it left is empty', async () => {
      await mount([{ ...readyDocument, isAssigned: true }]);

      await move(readyDocument);

      expect(document.activeElement?.id).toBe('knowledge-selected-heading');
      expect(titles('available')).toEqual(['Returns policy']);
    });

    it('shows both lists to somebody who may only read, with nothing to press', async () => {
      await mount([{ ...readyDocument, isAssigned: true }, unindexed], '"agent-v1"', [
        'knowledge.view',
        'ai-agents.view',
      ]);

      expect(titles('selected')).toEqual(['Returns policy']);
      expect(titles('available')).toEqual(['Delivery times']);
      expect(host().querySelectorAll('[data-move]')).toHaveLength(0);
      expect(query('[data-testid="save-assignment"]')).toBeNull();
    });
  });

  describe('saving', () => {
    it('sends the agent version and the whole selection', async () => {
      await mount();

      await move(readyDocument);

      saveButton().click();

      const save = http.expectOne(knowledgeUrl);

      expect(save.request.method).toBe('PUT');
      expect(save.request.headers.get('If-Match')).toBe('"agent-v1"');
      expect(save.request.body).toEqual({
        knowledgeDocumentIds: [readyDocument.knowledgeDocumentId],
      });

      save.flush(
        { aiAgentId: agentId, documents: [{ ...readyDocument, isAssigned: true }, unindexed] },
        { headers: { ETag: '"agent-v2"' } },
      );

      await settle();
    });

    it('reports the agent new version, so the editor holding it stays usable', async () => {
      // Without this the editor would keep a tag one version behind, and the
      // reader's next save of the name or instructions would be refused for a
      // change they made themselves.
      const versions: string[] = [];

      await mount();

      fixture.componentInstance.versionChanged.subscribe((etag) => versions.push(etag));

      await move(readyDocument);

      saveButton().click();

      http
        .expectOne(knowledgeUrl)
        .flush(
          { aiAgentId: agentId, documents: [{ ...readyDocument, isAssigned: true }, unindexed] },
          { headers: { ETag: '"agent-v2"' } },
        );

      await settle();

      expect(versions).toEqual(['"agent-v2"']);
    });

    it('sends nothing when nothing was changed', async () => {
      await mount();

      expect(saveButton().disabled).toBe(true);

      saveButton().click();
      await settle();

      http.verify();
    });

    it('cannot write while the agent version is unknown', async () => {
      await mount([readyDocument, unindexed], null);

      await move(readyDocument);

      expect(saveButton().disabled).toBe(true);
    });
  });

  describe('when a save is rejected as stale (412)', () => {
    async function conflict(): Promise<void> {
      await mount();

      await move(readyDocument);

      saveButton().click();

      http
        .expectOne(knowledgeUrl)
        .flush(
          { detail: 'Changed elsewhere.' },
          { status: 412, statusText: 'Precondition Failed' },
        );

      await settle();
    }

    it('keeps the ticks exactly where the reader put them', async () => {
      // Replacing them with server state would silently discard the selection
      // they were in the middle of making.
      await conflict();

      expect(titles('selected')).toEqual(['Returns policy']);
      expect(query('[data-testid="assignment-stale"]')).not.toBeNull();
      expect(host().textContent).toContain('exactly as you left it');
    });

    it('retries nothing on its own', async () => {
      await conflict();

      http.verify();
    });

    it('reloads only when asked, and says that will replace the selection', async () => {
      await conflict();

      expect(host().textContent).toContain('which will replace it');

      [...host().querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.includes('Reload latest'))!
        .click();

      await settle();

      http
        .expectOne(knowledgeUrl)
        .flush(
          { aiAgentId: agentId, documents: [readyDocument, { ...unindexed, isAssigned: true }] },
          { headers: { ETag: '"agent-v9"' } },
        );

      await settle();

      expect(titles('selected')).toEqual(['Delivery times']);
      expect(titles('available')).toEqual(['Returns policy']);
    });
  });

  describe('retrieval tester', () => {
    async function search(question: string): Promise<void> {
      const box = query<HTMLInputElement>('[data-testid="retrieval-question"]')!;

      box.value = question;
      box.dispatchEvent(new Event('input'));

      await settle();

      [...host().querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.includes('Test retrieval'))!
        .click();

      await settle();
    }

    it('asks the agent search endpoint and shows what came back', async () => {
      await mount();

      await search('Can I return this?');

      const request = http.expectOne(`${knowledgeUrl}/search`);

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ question: 'Can I return this?' });

      request.flush({
        hits: [
          {
            knowledgeDocumentId: readyDocument.knowledgeDocumentId,
            title: 'Returns policy',
            ordinal: 0,
            excerpt: 'Goods may be returned within 30 days.',
            rank: 1,
            similarity: 0.82,
          },
        ],
      });

      await settle();

      expect(query('[data-testid="retrieval-hits"]')?.textContent).toContain('30 days');
      expect(query('[data-testid="retrieval-hits"]')?.textContent).toContain('82% match');
      expect(query('[data-testid="retrieval-hits"]')?.textContent).toContain('Rank 1');
    });

    it('says plainly when nothing was retrieved', async () => {
      await mount();

      await search('Anything at all?');

      http.expectOne(`${knowledgeUrl}/search`).flush({ hits: [] });
      await settle();

      expect(query('[data-testid="retrieval-empty"]')?.textContent).toContain(
        'Nothing was retrieved',
      );
    });

    it('renders a retrieved excerpt as text, never as markup', async () => {
      // Retrieved text is whatever somebody typed into a document. Rendering it
      // as markup would turn a knowledge document into a script.
      await mount();

      await search('Anything?');

      http.expectOne(`${knowledgeUrl}/search`).flush({
        hits: [
          {
            knowledgeDocumentId: readyDocument.knowledgeDocumentId,
            title: '<img src=x onerror="alert(1)">',
            ordinal: 0,
            excerpt: '<script>alert(1)</script>',
            rank: 1,
            similarity: 0.5,
          },
        ],
      });

      await settle();

      const hits = query('[data-testid="retrieval-hits"]')!;

      expect(hits.querySelector('script')).toBeNull();
      expect(hits.querySelector('img')).toBeNull();
      expect(hits.textContent).toContain('<script>alert(1)</script>');
    });

    it('sends nothing for a blank question', async () => {
      await mount();

      await search('   ');

      http.verify();
    });
  });
});
