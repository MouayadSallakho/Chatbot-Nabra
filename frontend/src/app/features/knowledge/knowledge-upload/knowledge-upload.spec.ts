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
import { KnowledgeUpload } from './knowledge-upload';

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

      expect(host().textContent).toContain('The file is not kept');
    });

    it('says indexing is a separate step', async () => {
      await mount();

      expect(host().textContent).toContain('Indexing is a separate step');
    });

    it('says scanned PDFs are not read', async () => {
      await mount();

      expect(host().textContent).toContain('Scanned PDFs are not read');
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
      expect(query('[data-testid="chosen-file"]')?.textContent).toContain('2 kB');
      expect(query('[data-testid="chosen-file"]')?.textContent).toContain('PDF');
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

      expect(query('[data-testid="local-refusal"]')?.textContent).toContain('PDF, Word (.docx)');
      http.expectNone(filesUrl);
    });
  });

  describe('the drop zone', () => {
    /** A drag event carrying files, as far as the component reads one. */
    function drag(type: string, files: File[]): Event {
      const event = new Event(type, { bubbles: true, cancelable: true });

      Object.defineProperty(event, 'dataTransfer', {
        value: {
          types: ['Files'],
          files: { length: files.length, item: (index: number) => files[index] ?? null },
          dropEffect: 'none',
        },
      });

      return event;
    }

    const zone = (): HTMLElement => query<HTMLElement>('[data-testid="drop-zone"]')!;

    it('offers a real button that opens the file picker', async () => {
      await mount();

      const input = query<HTMLInputElement>('[data-testid="file-input"]')!;
      const opened = vi.fn();

      input.addEventListener('click', opened);
      query<HTMLButtonElement>('[data-testid="choose-file"]')!.click();

      expect(opened).toHaveBeenCalledTimes(1);
      // The native input is not a second tab stop.
      expect(input.tabIndex).toBe(-1);
    });

    it('takes a dropped file through the same checks as a chosen one', async () => {
      await mount();

      zone().dispatchEvent(drag('dragenter', [pdf()]));
      await settle();

      expect(zone().classList).toContain('is-dragging');

      const dropped = drag('drop', [pdf('policy.pdf')]);

      zone().dispatchEvent(dropped);
      await settle();

      expect(dropped.defaultPrevented).toBe(true);
      expect(zone().classList).not.toContain('is-dragging');
      expect(query('[data-testid="chosen-file-name"]')?.textContent).toContain('policy.pdf');
      expect(uploadButton().disabled).toBe(false);
    });

    it('refuses a dropped file it cannot read, and takes only the first of several', async () => {
      await mount();

      zone().dispatchEvent(
        drag('drop', [new File([new Uint8Array(4)], 'photo.png'), pdf('second.pdf')]),
      );
      await settle();

      expect(query('[data-testid="local-refusal"]')).not.toBeNull();
      expect(host().textContent).toContain('Only the first file was taken');
      http.expectNone(filesUrl);
    });

    it('removes the chosen file and puts focus back on the choose button', async () => {
      await mount();

      await choose(pdf());

      query<HTMLButtonElement>('[data-testid="remove-file"]')!.click();
      await settle();

      expect(query('[data-testid="chosen-file"]')).toBeNull();
      expect(document.activeElement).toBe(query('[data-testid="choose-file"]'));
      expect(uploadButton().disabled).toBe(true);
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

    it('reports an unsupported format with the formats it accepts', async () => {
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
        'not a PDF, Word (.docx), plain text or CSV file',
      );
    });

    it('reports an oversized file the server refused with the real limit', async () => {
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

      expect(query('[data-testid="upload-error"]')?.textContent).toContain('larger than 10 MB');
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
