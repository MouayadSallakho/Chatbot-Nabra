import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { ConversationRealtime } from '@app/core/realtime/conversation-realtime';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WhatsAppApi } from '@app/features/channels/whatsapp-api';
import { ConversationsApi } from '../conversations-api';
import { ConversationDetail } from './conversation-detail';

describe('ConversationDetail', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const conversationId = '22222222-2222-7222-8222-222222222222';

  const detailUrl = `/api/tenants/${tenantId}/conversations/${conversationId}`;
  // The server's largest page: a first read follows the pages to the newest message.
  const messagesUrl = `${detailUrl}/messages?limit=200`;

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

  beforeEach(() => {
    notify = null;
    unsubscribed = false;
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

  it('reads every page to the newest message, in one continuous order', async () => {
    // The API pages forward from the first message, so the screen follows the
    // pages itself: the newest messages are what the reader needs to see.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail, tagged(detail));
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'Page one', 10)],
      nextCursor: 10,
    });
    await settle();

    // The cursor is the stored ingestion sequence, not a page number.
    http.expectOne(`${detailUrl}/messages?limit=200&after=10`).flush({
      items: [message('m2', 'AiAgent', 'Page two', 11)],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['Page one', 'Page two']);
    expect(button('Load newer messages')).toBeUndefined();
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

    http.expectOne(messagesUrl).flush({
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

    http.expectOne(messagesUrl).flush({
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
});
