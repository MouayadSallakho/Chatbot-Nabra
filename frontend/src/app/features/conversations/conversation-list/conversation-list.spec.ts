import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ConversationsApi } from '../conversations-api';
import { ConversationList } from './conversation-list';

describe('ConversationList', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const url = `/api/tenants/${tenantId}/conversations?page=1&pageSize=20`;

  let fixture: ComponentFixture<ConversationList>;
  let http: HttpTestingController;

  const conversation = (id: string, preview: string | null, lastActivityAt: string) => ({
    id,
    channel: 'WebChat',
    status: 'Open',
    startedAt: '2026-09-07T10:00:00Z',
    lastActivityAt,
    agentName: 'Concierge',
    messageCount: preview ? 2 : 0,
    lastMessagePreview: preview,
    lastMessageSender: preview ? 'AiAgent' : null,
  });

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ConversationsApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConversationList);
    fixture.detectChanges();
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  const rows = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('a.conversation-row')];

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  afterEach(() => http.verify());

  it('lists conversations in the order the server returned them', async () => {
    // Most recently active first, decided by the server so paging stays stable.
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [
        conversation('c1', 'We close at six.', '2026-09-07T12:00:00Z'),
        conversation('c2', 'Certainly.', '2026-09-07T11:00:00Z'),
      ],
      page: 1,
      pageSize: 20,
      total: 2,
    });
    await settle();

    expect(rows()).toHaveLength(2);
    expect(rows()[0].textContent).toContain('We close at six.');
    expect(rows()[1].textContent).toContain('Certainly.');
    expect(text()).toContain('2 conversations');
  });

  it('shows the channel as a name rather than an internal value', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [conversation('c1', 'Hello', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    expect(text()).toContain('Website chat');
    expect(text()).not.toContain('WebChat');
  });

  it('renders a script-like preview as text', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [conversation('c1', '<script>alert(1)</script>', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    expect(text()).toContain('<script>alert(1)</script>');
    expect(host().querySelector('script')).toBeNull();
  });

  it('explains an empty inbox rather than showing a bare list', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await settle();

    expect(text()).toContain('No conversations yet');
  });

  it('offers a retry when the list could not be read', async () => {
    await mount('conversations.view');

    http
      .expectOne(url)
      .flush(
        { title: 'Server error', detail: 'Something went wrong.' },
        { status: 500, statusText: 'Server Error' },
      );
    await settle();

    expect(text()).toContain('Could not load conversations');
    expect(button('Try again')).toBeTruthy();
  });

  it('refreshes by hand rather than polling', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await settle();

    button('Refresh')!.click();
    await settle();

    http.expectOne(url).flush({
      items: [conversation('c1', 'Just arrived', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    expect(text()).toContain('Just arrived');
  });

  it('explains a refusal rather than showing an empty inbox', async () => {
    await mount();
    await settle();

    expect(text()).toContain('do not have access to conversations');
  });
});
