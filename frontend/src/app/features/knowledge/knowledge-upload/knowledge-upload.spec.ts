import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { KnowledgeApi } from '../knowledge-api';
import { KNOWLEDGE_FILE_LIMITS } from '../knowledge-limits';
import { KnowledgeUpload, describeBytes } from './knowledge-upload';

/**
 * The knowledge upload page.
 *
 * What is worth testing here is not that a POST happens. It is that the page
 * tells the truth about three things somebody would otherwise discover the
 * expensive way: the file is not kept, indexing has not happened, and a scanned
 * PDF will not work. Plus the refusals, each of which has to be distinguishable
 * -- "too big", "wrong format" and "no text in it" lead to different next steps.
 */
describe('KnowledgeUpload', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const filesUrl = `/api/tenants/${tenantId}/knowledge/files`;

  let fixture: ComponentFixture<KnowledgeUpload>;
  let http: HttpTestingController;

  async function mount(permissions: string = '*'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [KnowledgeUpload],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(permissions) },
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

    fixture = TestBed.createComponent(KnowledgeUpload);

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

  const uploadButton = (): HTMLButtonElement =>
    query<HTMLButtonElement>('[data-testid="upload-button"]')!;

  /**
   * Puts a file into the input the way a browser would.
   *
   * `HTMLInputElement.files` is read-only, and the test DOM has no
   * `DataTransfer` to build a real `FileList` with -- so the property is defined
   * over with the smallest thing the component actually reads. Writing the
   * component to accept a plain array instead would be changing production code
   * to suit a test runner.
   */
  async function choose(chosen: File | null): Promise<void> {
    const input = query<HTMLInputElement>('[data-testid="file-input"]')!;

    Object.defineProperty(input, 'files', { configurable: true, value: fileList(chosen) });

    input.dispatchEvent(new Event('change'));

    await settle();
  }

  function fileList(chosen: File | null): FileList {
    return {
      length: chosen ? 1 : 0,
      item: (index: number) => (chosen && index === 0 ? chosen : null),
    } as unknown as FileList;
  }

  function pdf(name = 'returns.pdf', size = 2_048): File {
    return new File([new Uint8Array(size)], name, { type: 'application/pdf' });
  }

  afterEach(() => http.verify());

  describe('what the page says', () => {
    it('names the formats it accepts and the size limit', async () => {
      await mount();

      expect(host().textContent).toContain('PDF');
      expect(host().textContent).toContain('CSV');
      expect(host().textContent).toContain(`${KNOWLEDGE_FILE_LIMITS.maxMegabytes} MB`);
    });

    it('says the file itself is not stored', async () => {
      await mount();

      expect(host().textContent).toContain('The file itself is not stored');
    });

    it('says indexing is a separate step', async () => {
      await mount();

      expect(host().textContent).toContain('Indexing is a separate step');
    });

    it('says scanned PDFs are not supported', async () => {
      await mount();

      expect(host().textContent).toContain('Scanned PDFs are not supported');
    });

    it('refuses the page to somebody who may not manage knowledge', async () => {
      await mount('knowledge.view');

      expect(host().textContent).toContain('You cannot add knowledge');
      expect(query('[data-testid="file-input"]')).toBeNull();
    });
  });

  describe('choosing a file', () => {
    it('will not upload until one is chosen', async () => {
      await mount();

      expect(uploadButton().disabled).toBe(true);
    });

    it('shows the name and size of the chosen file', async () => {
      await mount();

      await choose(pdf('returns.pdf', 2_048));

      expect(query('[data-testid="chosen-file"]')?.textContent).toContain('returns.pdf');
      expect(query('[data-testid="chosen-file"]')?.textContent).toContain('2 KB');
    });

    it('refuses a file past the limit without sending it', async () => {
      await mount();

      await choose(pdf('huge.pdf', KNOWLEDGE_FILE_LIMITS.maxBytes + 1));

      expect(query('[data-testid="local-refusal"]')?.textContent).toContain('10 MB or smaller');
      expect(uploadButton().disabled).toBe(true);

      // The point of the check: nothing was put on the wire.
      http.expectNone(filesUrl);
    });

    it('refuses an empty file without sending it', async () => {
      await mount();

      await choose(pdf('nothing.pdf', 0));

      expect(query('[data-testid="local-refusal"]')?.textContent).toContain('empty');
      http.expectNone(filesUrl);
    });

    it('refuses a format it cannot read without sending it', async () => {
      await mount();

      await choose(new File([new Uint8Array(16)], 'slides.pptx'));

      expect(query('[data-testid="local-refusal"]')?.textContent).toContain('PDF, Word document');
      http.expectNone(filesUrl);
    });
  });

  describe('uploading', () => {
    it('sends the file as multipart and lets the browser set the boundary', async () => {
      await mount();

      await choose(pdf());

      uploadButton().click();

      const request = http.expectOne(filesUrl);

      expect(request.request.method).toBe('POST');
      expect(request.request.body instanceof FormData).toBe(true);

      // Null, deliberately: a Content-Type set here would replace the browser's
      // own and lose the multipart boundary with it.
      expect(request.request.headers.get('Content-Type')).toBeNull();

      const body = request.request.body as FormData;

      expect((body.get('file') as File).name).toBe('returns.pdf');

      request.flush(
        {
          id: '22222222-2222-2222-2222-222222222222',
          tenantId,
          title: 'returns',
          content: 'Goods may be returned within 30 days.',
          isEnabled: true,
          readiness: 'NeedsIndex',
          createdAt: '2026-09-14T09:00:00+00:00',
          updatedAt: '2026-09-14T09:00:00+00:00',
          indexedAt: null,
          source: {
            fileName: 'returns.pdf',
            fileType: 'Pdf',
            byteLength: 2_048,
            extractedAt: '2026-09-14T09:00:00+00:00',
          },
        },
        { headers: { ETag: '"v1"' } },
      );

      await settle();
    });

    it('sends a title only when one was typed', async () => {
      await mount();

      await choose(pdf());

      const title = query<HTMLInputElement>('#knowledge-upload-title')!;

      title.value = 'Refund policy';
      title.dispatchEvent(new Event('input'));

      await settle();

      uploadButton().click();

      const request = http.expectOne(filesUrl);

      expect((request.request.body as FormData).get('title')).toBe('Refund policy');

      request.flush(null, { status: 500, statusText: 'Server Error' });

      await settle();
    });

    it('reports a file with no readable text in the server words', async () => {
      await mount();

      await choose(pdf('scan.pdf'));

      uploadButton().click();

      http.expectOne(filesUrl).flush(
        {
          title: 'No text could be read',
          detail:
            'No extractable text was found. Scanned or image-only documents are not supported yet.',
        },
        { status: 422, statusText: 'Unprocessable Content' },
      );

      await settle();

      expect(query('[data-testid="upload-error"]')?.textContent).toContain(
        'No extractable text was found',
      );

      // And it says what state the workspace is in afterwards, which is: unchanged.
      expect(query('[data-testid="upload-error"]')?.textContent).toContain(
        'no document was created',
      );
    });

    it('reports an unsupported format in the server words', async () => {
      await mount();

      await choose(pdf('actually-a-zip.pdf'));

      uploadButton().click();

      http
        .expectOne(filesUrl)
        .flush(
          { title: 'Unsupported file', detail: 'That file is not a PDF, Word document or CSV.' },
          { status: 415, statusText: 'Unsupported Media Type' },
        );

      await settle();

      expect(query('[data-testid="upload-error"]')?.textContent).toContain(
        'not a PDF, Word document or CSV',
      );
    });

    it('reports an oversized file the server refused in the server words', async () => {
      await mount();

      await choose(pdf());

      uploadButton().click();

      http
        .expectOne(filesUrl)
        .flush(
          { title: 'That file is too large', detail: 'Uploads must be 10 MB or smaller.' },
          { status: 413, statusText: 'Payload Too Large' },
        );

      await settle();

      expect(query('[data-testid="upload-error"]')?.textContent).toContain('10 MB or smaller');
    });

    it('does not send a second request while the first is in flight', async () => {
      await mount();

      await choose(pdf());

      uploadButton().click();
      uploadButton().click();

      // One, not two. A double click must not create two documents.
      const request = http.expectOne(filesUrl);

      request.flush(null, { status: 500, statusText: 'Server Error' });

      await settle();
    });
  });
});

describe('describeBytes', () => {
  it('reads sizes in the unit a person would use', () => {
    expect(describeBytes(512)).toBe('512 bytes');
    expect(describeBytes(2_048)).toBe('2 KB');
    expect(describeBytes(1_572_864)).toBe('1.5 MB');
  });
});
