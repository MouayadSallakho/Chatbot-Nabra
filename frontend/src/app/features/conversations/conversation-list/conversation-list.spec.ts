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

  const rows = (): HTMLElement[] => [
    ...host().querySelectorAll<HTMLElement>('[data-testid="conversation-rows"] a.conversation-row'),
  ];

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

  it('keeps the rows on screen when a refresh fails, and says so', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [conversation('c1', 'We close at six.', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    button('Refresh')!.click();
    await settle();
    http.expectOne(url).error(new ProgressEvent('error'), { status: 0 });
    await settle();

    expect(rows()).toHaveLength(1);
    expect(text()).toContain('Could not refresh the list.');
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

  it('marks the open conversation, and gives up the page heading beside it', async () => {
    await mount('conversations.view');
    fixture.componentRef.setInput('selectedId', 'c2');
    fixture.componentRef.setInput('isPage', false);

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

    const current = host().querySelectorAll('a[aria-current="page"]');

    expect(current).toHaveLength(1);
    expect(current[0].textContent).toContain('Certainly.');
    // The open conversation's heading is the page's; the list's is a section's.
    expect(host().querySelector('h1')).toBeNull();
    expect(host().querySelector('h2')?.textContent).toContain('Conversations');
  });

  it('says the team owes a reply only where the data says so', async () => {
    // A person is answering and the customer wrote last: the AI will not
    // answer in that mode, so somebody on the team must. Nothing else is
    // marked: the API carries no unread state to mark with.
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [
        {
          ...conversation('c1', 'Is anyone there?', '2026-09-07T12:00:00Z'),
          handling: 'Human',
          lastMessageSender: 'Customer',
        },
        {
          ...conversation('c2', 'Answered.', '2026-09-07T11:00:00Z'),
          handling: 'Human',
          lastMessageSender: 'HumanAgent',
        },
        { ...conversation('c3', 'Hello?', '2026-09-07T10:00:00Z'), lastMessageSender: 'Customer' },
      ],
      page: 1,
      pageSize: 20,
      total: 3,
    });
    await settle();

    const waiting = rows().map((row) => row.textContent?.includes('Waiting for the team'));

    expect(waiting).toEqual([true, false, false]);
  });

  it('names who is answering each conversation in a word, with its glyph', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [
        { ...conversation('c1', 'Mine now.', '2026-09-07T12:00:00Z'), handling: 'Human' },
        { ...conversation('c2', 'Hello.', '2026-09-07T11:00:00Z'), handling: 'Ai' },
      ],
      page: 1,
      pageSize: 20,
      total: 2,
    });
    await settle();

    const voices = rows().map((row) =>
      row.querySelector('app-voice-chip')?.getAttribute('data-voice'),
    );

    expect(voices).toEqual(['human', 'ai']);
    expect(rows()[0].textContent).toContain('Person');
    expect(rows()[1].textContent).toContain('AI');
  });

  it('moves between rows with the arrow keys, Home and End', async () => {
    await mount('conversations.view');
    document.body.append(host());

    http.expectOne(url).flush({
      items: [
        conversation('c1', 'One', '2026-09-07T12:00:00Z'),
        conversation('c2', 'Two', '2026-09-07T11:00:00Z'),
        conversation('c3', 'Three', '2026-09-07T10:00:00Z'),
      ],
      page: 1,
      pageSize: 20,
      total: 3,
    });
    await settle();

    const press = (key: string) =>
      document.activeElement!.dispatchEvent(
        new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
      );

    rows()[0].focus();
    press('ArrowDown');
    expect(document.activeElement).toBe(rows()[1]);
    press('End');
    expect(document.activeElement).toBe(rows()[2]);
    press('ArrowDown');
    expect(document.activeElement).toBe(rows()[2]);
    press('Home');
    expect(document.activeElement).toBe(rows()[0]);
    press('ArrowUp');
    expect(document.activeElement).toBe(rows()[0]);

    host().remove();
  });
});
