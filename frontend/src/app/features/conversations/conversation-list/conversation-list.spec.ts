import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { ArabicFonts } from '@app/core/i18n/arabic-fonts';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
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
  let released: number;
  let signals: { count: ReturnType<typeof signal<number | null>>; showCount: () => () => void };

  beforeEach(() => {
    released = 0;
    signals = {
      count: signal<number | null>(null),
      showCount: () => () => released++,
    };
  });

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
    customer: {
      id: `p-${id}`,
      displayName: null,
      labelCode: 'AnonymousVisitor',
      reference: null,
      shortCode: '7KQ2MX',
    },
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
        { provide: InboxSignals, useValue: signals },
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

  let changed: ((id: string | null) => void) | null = null;

  const realtimeStub = () => ({
    live: signal(true),
    listen: (_tenant: string, callback: (id: string | null) => void) => {
      changed = callback;

      return () => undefined;
    },
  });

  afterEach(() => http.verify());

  describe('the needs-a-person view', () => {
    const viewUrl = `${url}&filter=needs-person`;
    const emptyPage = { items: [], page: 1, pageSize: 20, total: 0 };
    const link = (id: string) => host().querySelector<HTMLElement>(`[data-testid="${id}"]`)!;
    const count = () => host().querySelector('[data-testid="needs-person-count"]');

    async function goToView(view: string | null): Promise<void> {
      await TestBed.inject(Router).navigate([], { queryParams: { view } });
      await settle();
    }

    it('offers both views as links, and says which one this is', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(link('view-all').getAttribute('aria-current')).toBe('true');
      expect(link('view-needs-person').hasAttribute('aria-current')).toBe(false);
      expect(link('view-needs-person').getAttribute('href')).toContain('view=needs-person');
    });

    it('shows the shared count beside its name, and costs no read of its own', async () => {
      signals.count.set(125);
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(count()?.textContent).toBe('125');
    });

    it('shows no number until one is known', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(count()).toBeNull();
    });

    it('shows a count of zero as a zero', async () => {
      signals.count.set(0);
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(count()?.textContent).toBe('0');
    });

    it('reads the filtered list when the address says so, its count from that total', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      await goToView('needs-person');

      http.expectOne(viewUrl).flush({
        items: [conversation('c1', 'Help', '2026-09-07T12:00:00Z')],
        page: 1,
        pageSize: 20,
        total: 3,
      });
      await settle();

      expect(link('view-needs-person').getAttribute('aria-current')).toBe('true');
      expect(count()?.textContent).toBe('3');
      expect(rows()).toHaveLength(1);
    });

    it('is calm about nobody waiting', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      await goToView('needs-person');
      http.expectOne(viewUrl).flush(emptyPage);
      await settle();

      expect(host().querySelector('[data-testid="needs-person-empty"]')?.textContent).toContain(
        'Nobody is waiting for a person',
      );
      expect(text()).not.toContain('No conversations yet');
    });

    it('goes back to the full list on Back, without mixing the two', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      await goToView('needs-person');
      http.expectOne(viewUrl).flush(emptyPage);
      await settle();

      await goToView(null);
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(link('view-all').getAttribute('aria-current')).toBe('true');
    });

    it('drops an answer for the view the reader has left', async () => {
      await mount('conversations.view');

      const first = http.expectOne(url);

      await goToView('needs-person');

      first.flush({
        items: [conversation('stale', 'Old', '2026-09-07T12:00:00Z')],
        page: 1,
        pageSize: 20,
        total: 1,
      });
      http.expectOne(viewUrl).flush(emptyPage);
      await settle();

      expect(rows()).toHaveLength(0);
    });

    it('re-reads the view it is showing', async () => {
      TestBed.configureTestingModule({
        providers: [{ provide: ConversationRealtime, useValue: realtimeStub() }],
      });
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();
      await goToView('needs-person');
      http.expectOne(viewUrl).flush(emptyPage);
      await settle();

      changed!('c1');
      await settle();

      http.expectOne(viewUrl).flush(emptyPage);
      await settle();
    });

    it('asks to be shown the count only for somebody who can read conversations, and lets go', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      fixture.destroy();

      expect(released).toBe(1);
    });

    it('offers no views to somebody who cannot read conversations', async () => {
      await mount();
      await settle();

      expect(host().querySelector('[data-testid="view-all"]')).toBeNull();
    });
  });

  describe('unread', () => {
    it('marks an unread row with a dot that has a name, and a heavier name', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush({
        items: [
          { ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'), unread: true },
          { ...conversation('c2', 'Hi', '2026-09-07T11:00:00Z'), unread: false },
        ],
        page: 1,
        pageSize: 20,
        total: 2,
      });
      await settle();

      const [unread, read] = rows();

      expect(unread.querySelector('[data-testid="row-unread"]')?.textContent).toBe('Unread');
      expect(unread.querySelector('.row-customer')?.classList.contains('is-unread')).toBe(true);
      expect(read.querySelector('[data-testid="row-unread"]')).toBeNull();
      expect(read.querySelector('.row-customer')?.classList.contains('is-unread')).toBe(false);
    });
  });

  it('leads every row with the customer, name or label', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [
        {
          ...conversation('c1', 'We close at six.', '2026-09-07T12:00:00Z'),
          customer: {
            id: 'p1',
            displayName: 'Lina Haddad',
            labelCode: 'Named',
            reference: null,
            shortCode: 'AB12CD',
          },
        },
        conversation('c2', 'Certainly.', '2026-09-07T11:00:00Z'),
      ],
      page: 1,
      pageSize: 20,
      total: 2,
    });
    await settle();

    const names = rows().map((row) =>
      row.querySelector('[data-testid="row-customer"]')?.textContent?.replace(/\s+/g, ' ').trim(),
    );

    expect(names).toEqual(['Lina Haddad', 'Visitor 7KQ2MX']);
    // First in the row, before the message and the chips.
    expect(
      rows()[0].firstElementChild?.querySelector('[data-testid="row-customer"]'),
    ).not.toBeNull();
  });

  it('starts the Arabic font when a first read holds Arabic', async () => {
    await mount('conversations.view');

    const notice = vi.spyOn(TestBed.inject(ArabicFonts), 'notice');

    http.expectOne(url).flush({
      items: [conversation('c1', 'مرحبا', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    expect(notice).toHaveBeenCalledWith(['مرحبا', null]);
  });

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
