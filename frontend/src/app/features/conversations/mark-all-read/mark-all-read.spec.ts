import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TenantApi } from '@app/core/api/tenant-api';
import { ToastService } from '@app/core/feedback/toast-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ConversationsApi } from '../conversations-api';
import { MarkAllRead, markedSentence, unreadSentence } from './mark-all-read';

describe('MarkAllRead', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const countUrl = `/api/tenants/${tenantId}/conversations/unread-count`;
  const allUrl = `/api/tenants/${tenantId}/conversations/read-all`;
  const original = {
    showModal: HTMLDialogElement.prototype.showModal,
    close: HTMLDialogElement.prototype.close,
  };

  let fixture: ComponentFixture<MarkAllRead>;
  let http: HttpTestingController;
  let toasts: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let marked: number[];

  beforeEach(async () => {
    // jsdom has no modal dialogs; these do what the confirmation relies on.
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    };

    toasts = { success: vi.fn(), error: vi.fn() };
    marked = [];

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ConversationsApi,
        { provide: ToastService, useValue: toasts },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting('conversations.view') },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MarkAllRead);
    fixture.componentInstance.marked.subscribe((n) => marked.push(n));
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    HTMLDialogElement.prototype.showModal = original.showModal;
    HTMLDialogElement.prototype.close = original.close;
  });

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const trigger = () => host().querySelector<HTMLButtonElement>('[data-testid="mark-all-read"]')!;
  const dialog = () => host().querySelector('dialog')!;

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  it('asks how many first, says the number, and marks them on confirmation', async () => {
    trigger().click();
    await settle();

    expect(trigger().getAttribute('aria-busy')).toBe('true');

    http.expectOne(countUrl).flush({ count: 961 });
    await settle();

    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().textContent).toContain('Mark every conversation as read?');
    expect(dialog().textContent).toContain('961 conversations in this workspace are unread.');
    expect(dialog().textContent).toContain('Only your own unread conversations change');

    [...dialog().querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Mark all'))!
      .click();
    await settle();

    const write = http.expectOne(allUrl);

    // A write with no version and no key: it is idempotent and only moves forward.
    expect(write.request.method).toBe('POST');
    expect(write.request.headers.has('If-Match')).toBe(false);
    write.flush({ marked: 961 });
    await settle();

    expect(toasts.success).toHaveBeenCalledWith('961 conversations marked as read.');
    expect(marked).toEqual([961]);
    expect(trigger().getAttribute('aria-busy')).toBe('false');
  });

  it('writes nothing when the reader cancels', async () => {
    trigger().click();
    await settle();
    http.expectOne(countUrl).flush({ count: 3 });
    await settle();

    [...dialog().querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Cancel'))!
      .click();
    await settle();

    http.expectNone(allUrl);
    expect(marked).toEqual([]);
    expect(toasts.success).not.toHaveBeenCalled();
  });

  it('opens no dialog when nothing is unread, and says so', async () => {
    trigger().click();
    await settle();
    http.expectOne(countUrl).flush({ count: 0 });
    await settle();

    expect(dialog().hasAttribute('open')).toBe(false);
    expect(toasts.success).toHaveBeenCalledWith('Every conversation is already read.');
    http.expectNone(allUrl);
  });

  it('ignores a second press while the first is on its way, without disabling the button', async () => {
    trigger().click();
    trigger().click();
    await settle();

    expect(trigger().disabled).toBe(false);
    http.expectOne(countUrl).flush({ count: 0 });
    await settle();
  });

  it('says a refusal in words, never the server English', async () => {
    trigger().click();
    await settle();
    http
      .expectOne(countUrl)
      .flush(
        { title: 'Forbidden', detail: 'The English sentence.' },
        { status: 403, statusText: 'Forbidden' },
      );
    await settle();

    expect(toasts.error).toHaveBeenCalledTimes(1);
    expect(toasts.error.mock.calls[0][0]).not.toContain('The English sentence');
    expect(dialog().hasAttribute('open')).toBe(false);
  });

  it('counts with the plural forms (English)', () => {
    expect(unreadSentence('en', 1)).toBe('1 conversation in this workspace is unread.');
    expect(unreadSentence('en', 1200)).toBe('1,200 conversations in this workspace are unread.');
    expect(markedSentence('en', 0)).toBe('Every conversation was already read.');
    expect(markedSentence('en', 1)).toBe('1 conversation marked as read.');
  });
});
