import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { FakeMediaMatcher, memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { WhatsAppApi } from '@app/features/channels/whatsapp-api';
import { ArabicFonts } from '@app/core/i18n/arabic-fonts';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { AuthService } from '@app/core/auth/auth-service';
import { CustomersApi } from '@app/features/customers/customers-api';
import { ConversationsApi, TranscriptMessage } from '../conversations-api';
import { ConversationComposer } from '../conversation-composer/conversation-composer';
import { ConversationTranscript } from '../conversation-transcript/conversation-transcript';
import { ConversationDetail } from './conversation-detail';

describe('ConversationDetail', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const conversationId = '22222222-2222-7222-8222-222222222222';

  const detailUrl = `/api/tenants/${tenantId}/conversations/${conversationId}`;
  // Every transcript opens at its newest forty messages, with what happened in them.
  const messagesUrl = `${detailUrl}/messages?tail=true&limit=40&include=events`;
  // A re-read continues forwards, from a little before the end of what is on screen.
  const catchUpUrl = `${detailUrl}/messages?limit=200&include=events`;

  let fixture: ComponentFixture<ConversationDetail>;
  let http: HttpTestingController;

  /** The callback the component registered, so a test can fire an event. */
  let notify: ((conversationId: string) => void) | null;
  let unsubscribed: boolean;

  const realtime = {
    live: signal(true),
    listen: (_tenantId: string, onChanged: (conversationId: string) => void) => {
      notify = onChanged;

      return () => {
        unsubscribed = true;
      };
    },
  };

  /** A phone's width (Phase 39.3): the one-line header. Off unless a test says so. */
  let phone = false;

  beforeEach(() => {
    notify = null;
    unsubscribed = false;
    phone = false;
  });

  const detail = {
    id: conversationId,
    channel: 'WebChat',
    status: 'Open',
    handling: 'Ai',
    startedAt: '2026-09-07T10:00:00Z',
    lastActivityAt: '2026-09-07T10:05:00Z',
    agentName: 'Concierge',
    messageCount: 2,
    assignedTo: null,
    customer: {
      id: 'p1',
      displayName: null,
      labelCode: 'AnonymousVisitor',
      reference: null,
      shortCode: '7KQ2MX',
    },
    lastReadSequence: null,
  };

  const humanDetail = { ...detail, handling: 'Human' };

  /**
   * Every read of a conversation carries its version, because taking one over
   * and routing it are conditional writes against exactly that revision.
   */
  const tagged = (body: unknown, etag = '"v1"') => ({ body, headers: { ETag: etag } });

  const message = (
    id: string,
    sender: string,
    text: string,
    sequence: number,
    sentBy: { userId: string; email: string } | null = null,
  ) => ({
    id,
    sender,
    text,
    sentAt: '2026-09-07T10:00:00Z',
    sequence,
    sentBy,
  });

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ConversationsApi,
        // The customer panel's one read (39.4b), over the same testing backend.
        CustomersApi,

        // The real client, over the same testing HTTP backend. These
        // conversations are on website chat, so it is never called -- and a test
        // that says so is worth more than a stub that could not.
        WhatsAppApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },

        // Stubbed, so a screen test is about the screen. The real client opens
        // a socket, which a test runner has no document to resolve an address
        // against; what matters here is that the component subscribes and
        // unsubscribes, and the captured callback is how a test drives it.
        { provide: ConversationRealtime, useValue: realtime },
        ...(phone
          ? [
              {
                provide: MEDIA_QUERY_FACTORY,
                useValue: new FakeMediaMatcher({ [MediaQueries.phone]: true }).factory,
              },
            ]
          : []),
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConversationDetail);
    fixture.componentRef.setInput('conversationId', conversationId);
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

  const bubbles = (): string[] =>
    [...host().querySelectorAll('.message-text')].map((node) => node.textContent?.trim() ?? '');

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  afterEach(() => http.verify());

  it('renders the transcript in the order the server returned it', async () => {
    // Ingestion order, which is what the API pages by. Nothing here re-sorts by
    // the timestamp shown beside each message.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [
        message('m1', 'Customer', 'First', 10),
        message('m2', 'AiAgent', 'Second', 11),
        message('m3', 'Customer', 'Third', 12),
      ],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['First', 'Second', 'Third']);
    expect(text()).toContain('Concierge');
  });

  it('renders script-like message text as text', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', '<script>alert(1)</script>', 10)],
      nextCursor: null,
    });
    await settle();

    expect(text()).toContain('<script>alert(1)</script>');
    expect(host().querySelector('script')).toBeNull();
  });

  it('heads the conversation with the customer, and keeps the channel in the facts under it', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();
    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null, previousCursor: null });
    await settle();

    const heading = host().querySelector('h1');

    expect(heading?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Visitor 7KQ2MX');
    expect(host().querySelector('.conversation-meta')?.textContent).toContain('Website chat');
  });

  describe('reading backwards', () => {
    const earlierUrl = (before: number) =>
      `${detailUrl}/messages?before=${before}&limit=40&include=events`;

    async function opened(): Promise<void> {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m3', 'Customer', 'Newest', 30)],
        nextCursor: null,
        previousCursor: 30,
        events: [],
      });
      await settle();
    }

    const showEarlier = () =>
      host().querySelector<HTMLButtonElement>('[data-testid="show-earlier"]');

    it('reads the page before, by the previous cursor, until there is none', async () => {
      await opened();

      showEarlier()!.click();
      await settle();

      http.expectOne(earlierUrl(30)).flush({
        items: [message('m2', 'AiAgent', 'Middle', 20)],
        nextCursor: 30,
        previousCursor: 20,
        events: [],
      });
      await settle();

      expect(bubbles()).toEqual(['Middle', 'Newest']);

      showEarlier()!.click();
      await settle();

      http.expectOne(earlierUrl(20)).flush({
        items: [message('m1', 'Customer', 'Oldest', 10)],
        nextCursor: 20,
        previousCursor: null,
        events: [],
      });
      await settle();

      expect(bubbles()).toEqual(['Oldest', 'Middle', 'Newest']);
      expect(showEarlier()).toBeNull();
      expect(text()).toContain('Start of the conversation');
    });

    it('asks once for a page, however many times it is asked', async () => {
      await opened();

      showEarlier()!.click();
      showEarlier()!.click();
      await settle();

      http
        .expectOne(earlierUrl(30))
        .flush({ items: [], nextCursor: 30, previousCursor: null, events: [] });
      await settle();
    });

    it('offers to try again after a failure, and keeps what is on screen', async () => {
      await opened();

      showEarlier()!.click();
      await settle();
      http
        .expectOne(earlierUrl(30))
        .flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(bubbles()).toEqual(['Newest']);
      expect(host().querySelector('[data-testid="earlier-failed"]')).not.toBeNull();

      showEarlier()!.click();
      await settle();
      http.expectOne(earlierUrl(30)).flush({
        items: [message('m2', 'AiAgent', 'Middle', 20)],
        nextCursor: 30,
        previousCursor: null,
        events: [],
      });
      await settle();

      expect(bubbles()).toEqual(['Middle', 'Newest']);
      expect(host().querySelector('[data-testid="earlier-failed"]')).toBeNull();
    });

    it('tells the voice track only part of the transcript is loaded', async () => {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m3', 'AiAgent', 'Newest', 30)],
        nextCursor: null,
        previousCursor: 30,
        events: [],
      });
      await settle();

      expect(text()).toContain('Replies in the messages loaded so far');

      showEarlier()!.click();
      await settle();
      http
        .expectOne(earlierUrl(30))
        .flush({ items: [], nextCursor: 30, previousCursor: null, events: [] });
      await settle();

      expect(text()).toContain('Replies so far');
    });
  });

  describe('what happened, from the server', () => {
    const failed = {
      id: 'e1',
      kind: 'AiTurnFailed',
      at: '2026-09-07T10:00:00Z',
      afterSequence: 10,
      actor: null,
      reason: 'generation_failed',
    };
    const took = {
      id: 'e2',
      kind: 'TakenOver',
      at: '2026-09-07T10:01:00Z',
      afterSequence: 10,
      actor: { userId: 'u1', email: 'sam@acme.test' },
      reason: null,
    };
    const dividers = () =>
      // The words of each line; its time follows them (39.4b).
      [...host().querySelectorAll('.handoff-divider .event-words > span:first-child')].map((node) =>
        node.textContent?.replace(/\s+/g, ' ').trim(),
      );

    it('draws the events of the first window', async () => {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [failed, took],
      });
      await settle();

      expect(dividers()).toEqual(['The AI could not reply', 'sam@acme.test took over']);
    });

    it('draws an event once when a re-read serves it again, and adds a new one', async () => {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [failed],
      });
      await settle();

      notify!(conversationId);
      await settle();
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(catchUpUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [failed, took],
      });
      await settle();

      expect(dividers()).toEqual(['The AI could not reply', 'sam@acme.test took over']);
    });

    it('invents no divider for a conversation from before events existed', async () => {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [
          message('m1', 'AiAgent', 'From the AI', 10),
          message('m2', 'HumanAgent', 'From a person', 11, {
            userId: 'u1',
            email: 'sam@acme.test',
          }),
        ],
        nextCursor: null,
        previousCursor: null,
        events: [],
      });
      await settle();

      expect(dividers()).toEqual([]);
    });
  });

  describe('marking it read', () => {
    const readUrl = `${detailUrl}/read`;

    async function opened(body: object = detail): Promise<void> {
      await mount('conversations.view');
      http.expectOne(detailUrl).flush(body, tagged(body));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [],
      });
      await settle();
    }

    const transcript = () =>
      fixture.debugElement.query(By.directive(ConversationTranscript))
        .componentInstance as ConversationTranscript;

    it('sends the newest sequence once it has really been seen', async () => {
      await opened();

      transcript().newestSeen.emit(10);
      await settle();

      const sent = http.expectOne(readUrl);

      expect(sent.request.method).toBe('POST');
      expect(sent.request.body).toEqual({ sequence: 10 });
      sent.flush(null, { status: 204, statusText: 'No Content' });
      await settle();
    });

    it('sends nothing the marker already covers', async () => {
      await opened({ ...detail, lastReadSequence: 10 });

      transcript().newestSeen.emit(10);
      await settle();

      http.expectNone(readUrl);
    });

    it('sends nothing twice for the same sequence once it has been accepted', async () => {
      await opened();

      transcript().newestSeen.emit(10);
      await settle();
      http.expectOne(readUrl).flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      transcript().newestSeen.emit(10);
      await settle();

      http.expectNone(readUrl);
    });

    it('says nothing on a failure, and sends again at the next sighting', async () => {
      await opened();

      transcript().newestSeen.emit(10);
      await settle();
      http.expectOne(readUrl).flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(host().querySelector('[role="alert"]')).toBeNull();

      transcript().newestSeen.emit(10);
      await settle();
      http.expectOne(readUrl).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
    });

    it('sends a newer sighting after the one in flight, not beside it', async () => {
      await opened();

      transcript().newestSeen.emit(10);
      await settle();
      transcript().newestSeen.emit(11);
      await settle();

      const first = http.expectOne(readUrl);

      expect(first.request.body).toEqual({ sequence: 10 });
      first.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      const second = http.expectOne(readUrl);

      expect(second.request.body).toEqual({ sequence: 11 });
      second.flush(null, { status: 204, statusText: 'No Content' });
      await settle();
    });

    it('sends nothing for a reply this member just wrote: the server marked it', async () => {
      await mount('conversations.view', 'conversations.reply');
      http.expectOne(detailUrl).flush(humanDetail, tagged(humanDetail));
      await settle();
      http
        .expectOne(messagesUrl)
        .flush({ items: [], nextCursor: null, previousCursor: null, events: [] });
      await settle();

      const composer = fixture.debugElement.query(By.directive(ConversationComposer))
        .componentInstance as ConversationComposer;

      composer.sent.emit(message('m9', 'HumanAgent', 'Mine', 20) as TranscriptMessage);
      await settle();

      transcript().newestSeen.emit(20);
      await settle();

      http.expectNone(readUrl);
    });
  });

  describe('what it tells the inbox signals', () => {
    const failed = {
      id: 'e1',
      kind: 'AiTurnFailed',
      at: '2026-09-07T10:00:00Z',
      afterSequence: 10,
      actor: null,
      reason: 'generation_failed',
    };

    it('says which conversation is open, and when it no longer is', async () => {
      await mount('conversations.view');

      const viewing = vi.spyOn(TestBed.inject(InboxSignals), 'viewing');

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http
        .expectOne(messagesUrl)
        .flush({ items: [], nextCursor: null, previousCursor: null, events: [] });
      await settle();

      fixture.destroy();

      expect(viewing).toHaveBeenCalledWith(null);
    });

    it('calls a first read history and a re-read live', async () => {
      await mount('conversations.view');

      const read = vi.spyOn(TestBed.inject(InboxSignals), 'transcriptRead');

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [failed],
      });
      await settle();

      expect(read).toHaveBeenLastCalledWith(
        expect.objectContaining({ id: conversationId }),
        [failed],
        false,
      );

      notify!(conversationId);
      await settle();
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(catchUpUrl).flush({
        items: [message('m1', 'Customer', 'Hello', 10)],
        nextCursor: null,
        previousCursor: null,
        events: [failed],
      });
      await settle();

      expect(read).toHaveBeenLastCalledWith(
        expect.objectContaining({ id: conversationId }),
        [failed],
        true,
      );
    });

    it("counts a takeover as this browser's own action, before it is sent", async () => {
      await mount('conversations.view', 'conversations.handoff');

      const own = vi.spyOn(TestBed.inject(InboxSignals), 'ownAction');

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http
        .expectOne(messagesUrl)
        .flush({ items: [], nextCursor: null, previousCursor: null, events: [] });
      await settle();

      button('Take over')!.click();
      await settle();

      expect(own).toHaveBeenCalledWith(conversationId);
      http.expectOne(`${detailUrl}/take-over`).flush(humanDetail, tagged(humanDetail, '"v2"'));
      await settle();
    });
  });

  it('starts the Arabic font as soon as the newest page holds Arabic', async () => {
    await mount('conversations.view');

    const notice = vi.spyOn(TestBed.inject(ArabicFonts), 'notice');

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'مرحبا', 10)],
      nextCursor: null,
      previousCursor: null,
      events: [],
    });
    await settle();

    expect(notice).toHaveBeenCalledWith(['مرحبا']);

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();
  });

  it('shows a conversation with no messages as empty rather than broken', async () => {
    await mount('conversations.view');

    const empty = { ...detail, messageCount: 0 };

    http.expectOne(detailUrl).flush(empty, tagged(empty));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(text()).toContain('no messages');
  });

  it('explains a conversation that is not there', async () => {
    await mount('conversations.view');

    // The first transcript page was asked for at the same time, and is refused
    // too; the conversation's own failure is the one the reader is told.
    http
      .expectOne(messagesUrl)
      .flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
    http
      .expectOne(detailUrl)
      .flush(
        { title: 'Not found', detail: 'No such conversation.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(text()).toContain('Conversation not found');
  });

  it('asks for the conversation and its first transcript page together', async () => {
    await mount('conversations.view');

    // Both are out before either has answered: the page no longer waits for the
    // conversation's facts before asking for its messages.
    const facts = http.expectOne(detailUrl);
    const firstPage = http.expectOne(messagesUrl);

    firstPage.flush({ items: [message('m1', 'Customer', 'Hello', 1)], nextCursor: null });
    facts.flush(detail, tagged(detail));
    await settle();

    expect(text()).toContain('Hello');
  });

  it('refreshes by hand rather than polling', async () => {
    // Deliberate: a timer firing against every open admin tab is a load pattern
    // nobody asked for, and it still would not be realtime.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10)],
      nextCursor: null,
    });
    await settle();

    button('Refresh')!.click();
    await settle();

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(catchUpUrl).flush({
      items: [message('m1', 'Customer', 'First', 10), message('m2', 'AiAgent', 'Newer', 11)],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['First', 'Newer']);
  });

  it('offers no reply box to somebody who may only read', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10)],
      nextCursor: null,
    });
    await settle();

    expect(host().querySelector('textarea')).toBeNull();
    expect(button('Send reply')).toBeUndefined();
    expect(button('Take over')).toBeUndefined();
  });

  it('withholds the composer while the AI is answering, and says why', async () => {
    // Pressing Send must not quietly take a conversation over: that stops the
    // AI answering a customer indefinitely, which is a decision somebody makes
    // on purpose rather than infers from a keystroke.
    await mount('conversations.view', 'conversations.reply');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(host().querySelector('textarea')).toBeNull();
    expect(text()).toContain('Take it over to reply yourself');
  });

  it('offers the composer once a person is handling it', async () => {
    await mount('conversations.view', 'conversations.reply');

    http.expectOne(detailUrl).flush(humanDetail, tagged(humanDetail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(host().querySelector('textarea')).not.toBeNull();
    expect(button('Send reply')).toBeDefined();
  });

  it('takes over against the version it read, and adopts the new one', async () => {
    await mount('conversations.view', 'conversations.handoff');

    http.expectOne(detailUrl).flush(detail, tagged(detail, '"v1"'));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    button('Take over')!.click();
    await settle();

    const write = http.expectOne(`${detailUrl}/take-over`);

    expect(write.request.method).toBe('POST');
    expect(write.request.headers.get('If-Match')).toBe('"v1"');

    write.flush(humanDetail, tagged(humanDetail, '"v2"'));
    await settle();

    expect(text()).toContain('A person');

    // The next write must present the version this response carried, not the
    // one the screen was opened with.
    button('Return to AI')!.click();
    await settle();

    const second = http.expectOne(`${detailUrl}/return-to-ai`);

    expect(second.request.headers.get('If-Match')).toBe('"v2"');
    second.flush(detail, tagged(detail, '"v3"'));
    await settle();
  });

  it('says in the heading who took it over, from the write, with the email isolated (BA-2)', async () => {
    await mount('conversations.view', 'conversations.handoff');

    http.expectOne(detailUrl).flush(detail, tagged(detail, '"v1"'));
    await settle();
    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    // AI mode: nobody is named, and no new words appear.
    expect(host().querySelector('[data-testid="handled-by"]')).toBeNull();

    button('Take over')!.click();
    await settle();

    const taken = { ...humanDetail, takenOverBy: { userId: 'u-me', email: 'me@acme.test' } };

    http.expectOne(`${detailUrl}/take-over`).flush(taken, tagged(taken, '"v2"'));
    await settle();

    const who = host().querySelector('[data-testid="handled-by"]');

    expect(who?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Handled by me@acme.test');
    expect(who?.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
    expect(who?.closest('header')).not.toBeNull();
  });

  it('names nobody for a person-handled conversation whose taker is not known', async () => {
    // A takeover from before Phase 37.4 recorded nobody: today's heading, unchanged.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(humanDetail, tagged({ ...humanDetail, takenOverBy: null }));
    await settle();
    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(host().querySelector('[data-testid="handled-by"]')).toBeNull();
    expect(text()).not.toContain('Handled by');
  });

  it('reports a refused precondition and retries nothing', async () => {
    await mount('conversations.view', 'conversations.handoff');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    button('Take over')!.click();
    await settle();

    http
      .expectOne(`${detailUrl}/take-over`)
      .flush(
        { title: 'Precondition failed', detail: 'This conversation was changed somewhere else.' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await settle();

    // The client's own wording for a stale precondition, not the server's
    // sentence: the shared error mapper turns 412 into one message every screen
    // in the application uses, so a reader meets the same words wherever it
    // happens. What matters here is that the refusal is reported at all.
    expect(text()).toContain('updated somewhere else');

    // Still on the version it read, and still showing the AI as the answerer:
    // a refused write changed nothing, so neither did the screen.
    expect(text()).toContain('The AI');

    // Nothing is sent again. A 412 means the intention was formed against a
    // state that no longer exists, so repeating it would apply it anyway.
    http.verify();
  });

  it('sends a reply with a key, and keeps the draft when it fails', async () => {
    await mount('conversations.view', 'conversations.reply');

    http.expectOne(detailUrl).flush(humanDetail, tagged(humanDetail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    const box = host().querySelector('textarea') as HTMLTextAreaElement;

    box.value = 'We refunded it this morning.';
    box.dispatchEvent(new Event('input'));
    await settle();

    button('Send reply')!.click();
    await settle();

    const sent = http.expectOne(`${detailUrl}/messages`);

    expect(sent.request.method).toBe('POST');
    expect(sent.request.body.text).toBe('We refunded it this morning.');
    expect(sent.request.body.clientMessageId).toBeTruthy();

    sent.flush(
      { title: 'Conflict', detail: 'This conversation is being answered by the AI.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    // The paragraph somebody typed is still there. Clearing a composer whose
    // send failed is how a reply gets written twice.
    expect((host().querySelector('textarea') as HTMLTextAreaElement).value).toBe(
      'We refunded it this morning.',
    );
  });

  it('keeps keyboard focus with the reader through a handover and a reply', async () => {
    await mount('conversations.view', 'conversations.handoff', 'conversations.reply');
    document.body.append(host());

    http.expectOne(detailUrl).flush(detail, tagged(detail, '"v1"'));
    await settle();
    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    button('Take over')!.click();
    await settle();
    http.expectOne(`${detailUrl}/take-over`).flush(humanDetail, tagged(humanDetail, '"v2"'));
    await settle();
    await settle();

    // Take over was replaced by Return to AI; the composer is what comes next.
    const box = (): HTMLTextAreaElement => host().querySelector('textarea')!;
    expect(document.activeElement).toBe(box());

    box().value = 'Checking that for you now.';
    box().dispatchEvent(new Event('input'));
    // Enter adds a line; it never sends. verify() in afterEach proves no request.
    box().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();

    button('Send reply')!.click();
    await settle();
    http
      .expectOne(`${detailUrl}/messages`)
      .flush(message('m1', 'HumanAgent', 'Checking that for you now.', 1));
    await settle();
    await settle();

    expect(box().value).toBe('');
    expect(document.activeElement).toBe(box());

    button('Return to AI')!.click();
    await settle();
    http.expectOne(`${detailUrl}/return-to-ai`).flush(detail, tagged(detail, '"v3"'));
    await settle();
    await settle();

    expect(document.activeElement).toBe(host().querySelector('[data-testid="take-over"]'));

    host().remove();
  });

  it('re-reads when told the conversation changed, without tearing the screen down', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10)],
      nextCursor: null,
    });
    await settle();

    expect(notify).not.toBeNull();

    notify!(conversationId);
    await settle();

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(catchUpUrl).flush({
      items: [
        message('m1', 'Customer', 'First', 10),
        message('m2', 'HumanAgent', 'On it', 11, { userId: 'u1', email: 'sam@acme.test' }),
      ],
      nextCursor: null,
    });
    await settle();

    // Merged, not duplicated: the first message was already on screen.
    expect(bubbles()).toEqual(['First', 'On it']);

    // The reader never saw a skeleton. A refresh of something already on screen
    // is not a load.
    expect(text()).not.toContain('Loading conversation');
  });

  it('ignores an event about a different conversation', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    notify!('99999999-9999-7999-8999-999999999999');
    await settle();

    // No second read. One workspace group carries every conversation's events,
    // so a screen that re-read on all of them would re-read constantly.
    http.verify();
  });

  it('names the colleague who replied, for the workspace', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'HumanAgent', 'On it', 10, { userId: 'u1', email: 'sam@acme.test' })],
      nextCursor: null,
    });
    await settle();

    // Inside the workspace this is the point of attribution. The customer is
    // shown something entirely different: see the public chat specs.
    expect(text()).toContain('sam@acme.test');
  });

  it('stops listening when the screen goes away', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    fixture.destroy();

    expect(unsubscribed).toBe(true);
  });

  it('explains a refusal rather than showing an empty transcript', async () => {
    await mount();
    await settle();

    expect(text()).toContain('do not have access to conversations');
  });

  describe('on a phone (Phase 38 note 5, 39.3)', () => {
    async function open(...permissions: string[]): Promise<void> {
      phone = true;
      await mount(...permissions);

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({ items: [], nextCursor: null, previousCursor: null });
      await settle();
    }

    const details = (): HTMLButtonElement =>
      host().querySelector<HTMLButtonElement>('[data-testid="toggle-details"]')!;

    it('heads the conversation with one line: back, the customer, the chip, Details', async () => {
      await open('conversations.view', 'conversations.handoff');

      expect(host().querySelector('.conversation')?.classList).toContain('is-compact');
      expect(host().querySelector('h1')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        'Visitor 7KQ2MX',
      );
      expect(host().querySelector('[data-testid="handling-chip-compact"]')?.textContent).toContain(
        'AI',
      );

      // The back link is the arrow alone, and still says where it goes.
      const back = host().querySelector<HTMLAnchorElement>('.conversation-back')!;

      expect(back.textContent?.replace(/\s+/g, ' ').trim()).toBe('Back to Conversations');
      expect(back.querySelector(':scope > span')?.classList).toContain('visually-hidden');

      // The facts wait for Details.
      expect(host().querySelector('.conversation-meta')).toBeNull();
      expect(host().querySelector('[data-testid="handling"]')).toBeNull();
      expect(host().querySelector('[data-testid="refresh-conversation"]')).toBeNull();
      expect(details().getAttribute('aria-expanded')).toBe('false');
    });

    it('opens the facts, the handling bar with its action and help, and Refresh on demand', async () => {
      await open('conversations.view', 'conversations.handoff');

      details().click();
      await settle();

      expect(details().getAttribute('aria-expanded')).toBe('true');
      expect(host().querySelector('.conversation-meta')?.textContent).toContain('Website chat');
      expect(host().querySelector('[data-testid="handling"]')).not.toBeNull();
      expect(
        host().querySelector('[data-testid="take-over"]')?.getAttribute('aria-describedby'),
      ).toBe('handling-help');
      expect(host().querySelector('#handling-help')).not.toBeNull();
      expect(host().querySelector('[data-testid="refresh-conversation"]')).not.toBeNull();
      // The chip is said once: on the title's line, not again in the bar.
      expect(host().querySelectorAll('app-voice-chip')).toHaveLength(1);

      details().click();
      await settle();

      expect(host().querySelector('[data-testid="handling"]')).toBeNull();
    });

    it('keeps the full header on a wider screen', async () => {
      await mount('conversations.view');

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({ items: [], nextCursor: null, previousCursor: null });
      await settle();

      expect(host().querySelector('.conversation')?.classList).not.toContain('is-compact');
      expect(host().querySelector('.conversation-meta')).not.toBeNull();
      expect(host().querySelector('[data-testid="handling"]')).not.toBeNull();
      expect(host().querySelector('[data-testid="handling-chip-compact"]')).toBeNull();
    });
  });

  describe('the thread and the customer panel (Calm Glass, 39.4b)', () => {
    const assigneesUrl = `/api/tenants/${tenantId}/conversations/assignees`;
    const customerUrl = `/api/tenants/${tenantId}/customers/p1`;
    const sara = { userId: 'u-sara', email: 'sara@acme.test', displayName: 'Sara' };
    const rana = { userId: 'u-rana', email: 'rana@acme.test', displayName: null };

    const customer = {
      id: 'p1',
      displayName: null,
      label: 'Visitor',
      labelCode: 'AnonymousVisitor',
      shortCode: '7KQ2MX',
      identities: [],
      channels: ['WebChat'],
      conversationCount: 3,
      lastActivityAt: '2026-09-07T10:05:00Z',
      createdAt: '2026-09-03T08:00:00Z',
      updatedAt: '2026-09-03T08:00:00Z',
      mergedIntoCustomerId: null,
      mergedAt: null,
      canReadConversations: true,
      conversations: [
        {
          id: conversationId,
          channel: 'WebChat',
          status: 'Open',
          handling: 'Ai',
          startedAt: '2026-09-07T10:00:00Z',
          lastActivityAt: '2026-09-07T10:05:00Z',
        },
        {
          id: 'c-older',
          channel: 'WebChat',
          status: 'Closed',
          handling: 'Ai',
          startedAt: '2026-09-04T09:00:00Z',
          lastActivityAt: '2026-09-04T09:30:00Z',
        },
      ],
    };

    async function opened(body: object, ...permissions: string[]): Promise<void> {
      await mount('conversations.view', ...permissions);

      if (permissions.includes('conversations.assign')) {
        http.expectOne(assigneesUrl).flush([sara, rana]);
      }

      http.expectOne(detailUrl).flush(body, tagged(body));
      await settle();
      http.expectOne(messagesUrl).flush({ items: [], nextCursor: null, previousCursor: null });
      await settle();
    }

    it('says who handles it by display name beside the chip, in its own direction (note 14)', async () => {
      await opened({ ...humanDetail, takenOverBy: sara });

      const state = host().querySelector('.handling-state');
      const who = host().querySelector('[data-testid="handled-by"]');

      expect(state?.querySelector('app-voice-chip')?.textContent?.trim()).toBe(
        'A person is answering',
      );
      expect(state?.contains(who)).toBe(true);
      expect(who?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Handled by Sara');
      expect(who?.querySelector('bdi')?.getAttribute('dir')).toBe('auto');
    });

    it('says the AI is answering in the chip while it is', async () => {
      await opened(detail);

      expect(host().querySelector('.handling-state app-voice-chip')?.textContent?.trim()).toBe(
        'The AI is answering',
      );
    });

    it('offers Assign as a menu of who can reply, the current one checked, and routes with the version', async () => {
      await opened(detail, 'conversations.assign');

      const trigger = host().querySelector<HTMLButtonElement>('[data-testid="assignee"]')!;

      expect(trigger.textContent?.trim()).toBe('Assign');
      expect(trigger.getAttribute('aria-haspopup')).toBe('menu');

      trigger.click();
      await settle();

      const items = [...host().querySelectorAll<HTMLElement>('[role="menuitemradio"]')];

      expect(items.map((item) => item.textContent?.trim())).toEqual([
        'Nobody',
        'Sara',
        'rana@acme.test',
      ]);
      expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual([
        'true',
        'false',
        'false',
      ]);
      expect(items[1].querySelector('bdi')?.getAttribute('dir')).toBe('auto');
      expect(items[2].querySelector('bdi')?.getAttribute('dir')).toBe('ltr');

      items[1].click();
      await settle();

      const put = http.expectOne(`${detailUrl}/assignment`);

      expect(put.request.method).toBe('PUT');
      expect(put.request.body).toEqual({ member: 'u-sara' });
      expect(put.request.headers.get('If-Match')).toBe('"v1"');

      const assigned = { ...detail, assignedTo: sara };

      put.flush(assigned, tagged(assigned, '"v2"'));
      await settle();

      expect(document.activeElement).toBe(host().querySelector('[data-testid="assignee"]'));
    });

    it('offers no Assign without the permission', async () => {
      await opened(detail);

      expect(host().querySelector('[data-testid="assignee"]')).toBeNull();
    });

    it("names the reader on their own reply until the re-read names it (the reply's response does not)", async () => {
      await mount('conversations.view', 'conversations.reply');

      (
        TestBed.inject(AuthService) as unknown as {
          account: { set: (user: unknown) => void };
        }
      ).account.set({ id: 'u-me', email: 'me@acme.test', displayName: 'Mona' });

      http.expectOne(detailUrl).flush(humanDetail, tagged(humanDetail));
      await settle();
      http
        .expectOne(messagesUrl)
        .flush({ items: [], nextCursor: null, previousCursor: null, events: [] });
      await settle();

      const composer = fixture.debugElement.query(By.directive(ConversationComposer))
        .componentInstance as ConversationComposer;

      composer.sent.emit(message('m9', 'HumanAgent', 'Mine', 20) as TranscriptMessage);
      await settle();

      expect(host().querySelector('[data-testid="message-author"]')?.textContent?.trim()).toBe(
        'Mona',
      );
    });

    it('reads the customer once for a reader with customers.view, and shows what it adds', async () => {
      await opened(detail, 'customers.view');

      host().querySelector<HTMLButtonElement>('[data-testid="toggle-details"]')!.click();
      await settle();

      http.expectOne(customerUrl).flush(customer, tagged(customer));
      await settle();

      expect(host().querySelector('[data-testid="customer-since"]')?.textContent).toContain(
        'Customer since',
      );
      expect(host().querySelector('[data-testid="customer-conversations"]')?.textContent).toBe('3');
      // The open conversation is not "earlier": only the other one is listed.
      const earlier = [
        ...host().querySelectorAll<HTMLAnchorElement>('[data-testid="earlier-conversation"]'),
      ];

      expect(earlier).toHaveLength(1);
      expect(earlier[0].getAttribute('href')).toBe('/conversations/c-older');

      // A live change to the conversation re-reads it, never the customer.
      notify?.(conversationId);
      await settle();
      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.match(() => true).forEach((request) => request.flush({ items: [], nextCursor: null }));
      await settle();
      http.expectNone(customerUrl);
    });

    it('never asks for the customer without customers.view, and still shows the conversation facts', async () => {
      await opened(detail);

      host().querySelector<HTMLButtonElement>('[data-testid="toggle-details"]')!.click();
      await settle();

      http.expectNone(customerUrl);
      expect(host().querySelector('[data-testid="customer-earlier"]')).toBeNull();
      expect(host().querySelector('[data-testid="context-assignee"]')?.textContent).toContain(
        'Nobody',
      );
      expect(host().querySelector('[data-testid="customer-channel"]')?.textContent).toBe(
        'Website chat',
      );
    });

    it("draws the thread's skeleton while it loads: header, bubbles with the orb, composer", async () => {
      await mount('conversations.view');

      const skeleton = host().querySelector('[data-testid="conversation-skeleton"]');

      expect(skeleton).not.toBeNull();
      expect(skeleton?.closest('[aria-busy="true"]')).not.toBeNull();
      expect(skeleton?.querySelector('.skeleton-title')).not.toBeNull();
      expect(skeleton?.querySelector('.skeleton-chip')).not.toBeNull();
      expect(skeleton?.querySelectorAll('.skeleton-bubble')).toHaveLength(3);
      expect(skeleton?.querySelector('.skeleton-end .skeleton-orb')).not.toBeNull();
      expect(skeleton?.querySelector('.skeleton-composer')).not.toBeNull();
      expect(host().querySelectorAll('h1')).toHaveLength(1);
      expect(text()).toContain('Loading conversation…');

      http.expectOne(detailUrl).flush(detail, tagged(detail));
      await settle();
      http.expectOne(messagesUrl).flush({ items: [], nextCursor: null, previousCursor: null });
      await settle();

      expect(host().querySelector('[data-testid="conversation-skeleton"]')).toBeNull();
    });
  });
});
