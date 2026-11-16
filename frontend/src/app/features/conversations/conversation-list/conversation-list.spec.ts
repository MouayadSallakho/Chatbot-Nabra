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
  let allRead: (() => void) | null = null;

  const realtimeStub = () => ({
    live: signal(true),
    listen: (_tenant: string, callback: (id: string | null) => void) => {
      changed = callback;

      return () => undefined;
    },
    listenRead: (_tenant: string, callback: () => void) => {
      allRead = callback;

      return () => undefined;
    },
  });

  afterEach(() => http.verify());

  // 39.8b: in Arabic the long "paused" wrapped the title row and "on" moved the
  // list up 24px when the connection came up (CLS 0.03). Both wordings now hold
  // the cell, hidden, so the state is as wide as its longer one in either state.
  it('keeps the live state as wide as its longer wording, and says only the one that holds', async () => {
    await mount('conversations.view');
    http.expectOne(url).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await settle();

    const state = host().querySelector('.live-state')!;
    const sizers = [...state.querySelectorAll('.live-sizer')];
    const shown = [...state.querySelectorAll('.live-words > span:not(.live-sizer)')];

    expect(state.getAttribute('role')).toBe('status');
    expect(sizers.map((sizer) => sizer.textContent?.trim())).toEqual([
      'Live updates on',
      'Live updates paused',
    ]);
    expect(sizers.every((sizer) => sizer.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(shown.length).toBe(1);
    expect(['Live updates on', 'Live updates paused']).toContain(shown[0].textContent?.trim());
  });

  describe('the needs-a-person view', () => {
    const viewUrl = `${url}&filter=needs-person`;
    const emptyPage = { items: [], page: 1, pageSize: 20, total: 0 };
    const link = (id: string) => host().querySelector<HTMLElement>(`[data-testid="${id}"]`)!;
    const count = () => host().querySelector('[data-testid="needs-person-count"]');

    async function goToView(view: string | null): Promise<void> {
      await TestBed.inject(Router).navigate([], { queryParams: { view } });
      await settle();
    }

    it('offers the three views as links, and says which one this is', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      expect(link('view-all').getAttribute('aria-current')).toBe('true');
      expect(link('view-needs-person').hasAttribute('aria-current')).toBe(false);
      expect(link('view-needs-person').getAttribute('href')).toContain('view=needs-person');
      expect(link('view-mine').hasAttribute('aria-current')).toBe(false);
      expect(link('view-mine').getAttribute('href')).toContain('view=mine');
      expect(link('view-mine').textContent?.trim()).toBe('Mine');
    });

    it("reads the reader's own conversations for Mine, and is calm when there are none (39.1)", async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      await goToView('mine');
      http.expectOne(`${url}&filter=mine`).flush({
        items: [conversation('c1', 'Mine', '2026-09-07T12:00:00Z')],
        page: 1,
        pageSize: 20,
        total: 1,
      });
      await settle();

      expect(link('view-mine').getAttribute('aria-current')).toBe('true');
      expect(rows()).toHaveLength(1);

      await goToView('needs-person');
      http.expectOne(viewUrl).flush(emptyPage);
      await goToView('mine');
      http.expectOne(`${url}&filter=mine`).flush(emptyPage);
      await settle();

      expect(host().querySelector('[data-testid="mine-empty"]')?.textContent).toContain(
        'Nothing is yours right now',
      );
    });

    it('reads the full list for a view this build does not know', async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();

      await goToView('everything');
      await settle();

      // No second read: an unknown view is the full list, already on screen.
      http.expectNone(`${url}&filter=everything`);
      expect(link('view-all').getAttribute('aria-current')).toBe('true');
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

    it("says each row's reason in words, from the server (BA-7)", async () => {
      await mount('conversations.view');
      http.expectOne(url).flush(emptyPage);
      await settle();
      await goToView('needs-person');

      const reasoned = (id: string, needsPersonReason: string | null) => ({
        ...conversation(id, 'Help', '2026-09-07T12:00:00Z'),
        handling: 'Ai',
        needsPerson: needsPersonReason !== null,
        needsPersonReason,
      });

      http.expectOne(viewUrl).flush({
        items: [
          { ...reasoned('c1', 'PersonHandling'), handling: 'Human', lastMessageSender: 'Customer' },
          reasoned('c2', 'AiTurnFailed'),
          reasoned('c3', 'ReturnedUnanswered'),
          reasoned('c4', 'SomethingNewer'),
          reasoned('c5', null),
        ],
        page: 1,
        pageSize: 20,
        total: 5,
      });
      await settle();

      const reasons = rows().map((row) => row.querySelector('[data-testid="row-reason"]'));
      const tones = reasons.map((chip) => chip?.getAttribute('data-tone'));

      // A chip: its dot beside the words, never colour alone. Coral only when the
      // AI could not reply; gold for every other wait.
      expect(tones).toEqual(['need', 'failed', 'need', 'need', 'need']);
      expect(reasons.every((chip) => chip?.classList.contains('chip-sm'))).toBe(true);

      expect(reasons.map((chip) => chip?.textContent?.trim())).toEqual([
        'Waiting for the team',
        'The AI could not reply',
        'Returned to the AI unanswered',
        // A reason this build does not know, or none, keeps today's wording.
        'Waiting for the team',
        'Waiting for the team',
      ]);
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

    it('re-reads when this member marked every conversation read in another tab (39.1)', async () => {
      TestBed.configureTestingModule({
        providers: [{ provide: ConversationRealtime, useValue: realtimeStub() }],
      });
      await mount('conversations.view');
      http.expectOne(url).flush({
        items: [{ ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'), unread: true }],
        page: 1,
        pageSize: 20,
        total: 1,
      });
      await settle();

      expect(host().querySelector('[data-testid="row-unread"]')).not.toBeNull();

      allRead!();
      await settle();
      http.expectOne(url).flush({
        items: [{ ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'), unread: false }],
        page: 1,
        pageSize: 20,
        total: 1,
      });
      await settle();

      // In place: the row stays, its dot goes, nothing flips back to skeletons.
      expect(rows()).toHaveLength(1);
      expect(host().querySelector('[data-testid="row-unread"]')).toBeNull();
      expect(host().querySelector('.is-skeleton')).toBeNull();
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
      // The dot closes the chips' line (board Inbox); the row itself gets no fill.
      expect(
        unread.querySelector('.row-chips')?.lastElementChild?.getAttribute('data-testid'),
      ).toBe('row-unread');
      expect(unread.classList.contains('is-unread')).toBe(true);
      expect(unread.classList.contains('is-selected')).toBe(false);
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
    // The avatar first (its letters: the name's initials, or the code's first two),
    // then the customer before the message and the chips.
    expect(rows()[0].firstElementChild?.tagName).toBe('APP-AVATAR');
    expect(rows()[0].querySelector('app-avatar')?.getAttribute('aria-hidden')).toBe('true');
    expect(rows().map((row) => row.querySelector('.avatar-letters')?.textContent)).toEqual([
      'LH',
      '7K',
    ]);
    expect(
      rows()[0]
        .querySelector('.row-main')
        ?.firstElementChild?.querySelector('[data-testid="row-customer"]'),
    ).not.toBeNull();
  });

  it('draws the skeleton in the shape of a real row (note 11)', async () => {
    await mount('conversations.view');

    const shape = (row: Element | null) =>
      [...(row?.querySelectorAll('*') ?? [])]
        .map((el) =>
          ['app-avatar', 'row-skeleton-avatar', 'row-main', 'row-top', 'row-preview', 'row-chips']
            .filter((name) => el.tagName.toLowerCase() === name || el.classList.contains(name))
            .map((name) => (name === 'row-skeleton-avatar' ? 'app-avatar' : name)),
        )
        .flat();
    const skeleton = shape(host().querySelector('.conversation-row.is-skeleton'));

    expect(host().querySelectorAll('.conversation-row.is-skeleton')).toHaveLength(6);

    http.expectOne(url).flush({
      items: [conversation('c1', 'Hello', '2026-09-07T12:00:00Z')],
      page: 1,
      pageSize: 20,
      total: 1,
    });
    await settle();

    expect(shape(rows()[0])).toEqual(skeleton);
    expect(skeleton).toEqual(['app-avatar', 'row-main', 'row-top', 'row-preview', 'row-chips']);
    // The text lines are the real lines' own elements, so they take the real line heights.
    const line = host().querySelector('.row-customer');

    expect(line).not.toBeNull();
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

    // In words for a screen reader: the avatar's badge is a glyph and is hidden.
    expect(rows()[0].querySelector('.visually-hidden')?.textContent).toContain('Website chat');
    expect(rows()[0].querySelector('.avatar-badge')?.getAttribute('data-channel')).toBe('WebChat');
    expect(text()).not.toContain('WebChat,');
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
    expect(host().querySelector('h2')?.textContent).toBe('Inbox');
    // A shape as well as a tint: the 2px edge.
    expect(current[0].classList.contains('edge-marker')).toBe(true);
  });

  it("says the navigation's word as the page's heading when nothing is open", async () => {
    await mount('conversations.view');
    http.expectOne(url).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await settle();

    expect(host().querySelector('h1')?.textContent).toBe('Inbox');
    expect(host().querySelector('section')?.getAttribute('aria-labelledby')).toBe('inbox-heading');
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

  it('names who took a conversation over on its row, with the email isolated (BA-2)', async () => {
    await mount('conversations.view');

    const sam = { userId: 'u-sam', email: 'sam@acme.test' };

    http.expectOne(url).flush({
      items: [
        {
          ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'),
          handling: 'Human',
          takenOverBy: sam,
        },
        { ...conversation('c2', 'Hi', '2026-09-07T11:00:00Z'), takenOverBy: null },
        // An older server, or a takeover from before 37.4: no field, no words.
        conversation('c3', 'Hi', '2026-09-07T10:00:00Z'),
      ],
      page: 1,
      pageSize: 20,
      total: 3,
    });
    await settle();

    const handled = rows().map((row) => row.querySelector('[data-testid="row-handled-by"]'));

    expect(handled[0]?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Handled by sam@acme.test');
    expect(handled[0]?.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
    expect(handled[0]?.getAttribute('data-tone')).toBe('team');
    expect(handled[1]).toBeNull();
    expect(handled[2]).toBeNull();
  });

  it('says who has a conversation once: the taker, else the assignee, by display name (note 13)', async () => {
    await mount('conversations.view');

    const sam = { userId: 'u-sam', email: 'sam@acme.test', displayName: 'Sam Lee' };
    const rana = { userId: 'u-rana', email: 'rana@acme.test', displayName: 'رنا' };
    const noName = { userId: 'u-x', email: 'x@acme.test', displayName: '  ' };

    http.expectOne(url).flush({
      items: [
        // Taken over and assigned to somebody else: the taker only.
        {
          ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'),
          handling: 'Human',
          takenOverBy: sam,
          assignedTo: rana,
        },
        // Assigned, the AI answering: the assignee, and the AI chip.
        {
          ...conversation('c2', 'Hi', '2026-09-07T11:00:00Z'),
          handling: 'Ai',
          assignedTo: rana,
        },
        // A blank display name is no name: the email, left to right.
        {
          ...conversation('c3', 'Hi', '2026-09-07T10:00:00Z'),
          handling: 'Human',
          takenOverBy: noName,
        },
      ],
      page: 1,
      pageSize: 20,
      total: 3,
    });
    await settle();

    const chips = (row: HTMLElement) =>
      [...row.querySelectorAll('.row-chips > :not(.row-unread)')].map((chip) =>
        chip.textContent?.replace(/\s+/g, ' ').trim(),
      );
    const [taken, assigned, blank] = rows();

    expect(chips(taken)).toEqual(['Handled by Sam Lee']);
    expect(taken.querySelector('[data-testid="row-handled-by"] bdi')?.getAttribute('dir')).toBe(
      'auto',
    );
    expect(chips(assigned)).toEqual(['Assigned to رنا', 'AI is answering']);
    expect(assigned.querySelector('[data-testid="row-assigned"] bdi')?.getAttribute('dir')).toBe(
      'auto',
    );
    expect(chips(blank)).toEqual(['Handled by x@acme.test']);
    expect(blank.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
  });

  it('never says the AI is answering beside a reason it is not (BA-7)', async () => {
    await mount('conversations.view');

    http.expectOne(url).flush({
      items: [
        {
          ...conversation('c1', 'Hi', '2026-09-07T12:00:00Z'),
          handling: 'Ai',
          needsPerson: true,
          needsPersonReason: 'AiTurnFailed',
        },
        {
          ...conversation('c2', 'Hi', '2026-09-07T11:00:00Z'),
          handling: 'Ai',
          needsPerson: false,
          needsPersonReason: null,
        },
      ],
      page: 1,
      pageSize: 20,
      total: 2,
    });
    await settle();

    const [failed, fine] = rows();

    expect(failed.querySelector('[data-testid="row-reason"]')?.textContent?.trim()).toBe(
      'The AI could not reply',
    );
    expect(failed.querySelector('[data-testid="row-voice"]')).toBeNull();
    expect(fine.querySelector('[data-testid="row-reason"]')).toBeNull();
    expect(fine.querySelector('[data-testid="row-voice"]')?.textContent?.trim()).toBe(
      'AI is answering',
    );
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
    expect(rows()[1].textContent).toContain('AI is answering');
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
