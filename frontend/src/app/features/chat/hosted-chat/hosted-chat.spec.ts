import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HostedChatApi } from '../hosted-chat-api';
import { HostedChatSessionStore } from '../hosted-chat-session';
import { HostedChat } from './hosted-chat';

describe('HostedChat', () => {
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';
  const conversationId = '11111111-1111-7111-8111-111111111111';
  const otherConversationId = '22222222-2222-7222-8222-222222222222';

  const configUrl = `/api/public/webchat/${publicKey}`;
  const startUrl = `/api/public/webchat/${publicKey}/conversations`;
  const messagesUrl = `/api/public/webchat/conversations/${conversationId}/messages`;

  /** Where the page is allowed to remember a conversation, and nowhere else. */
  const resumeKey = `omniai.webchat.conversation.${publicKey}`;

  let fixture: ComponentFixture<HostedChat>;
  let http: HttpTestingController;

  /** Puts the page at a hosted chat address, as a visitor's browser would. */
  function openAt(key: string): void {
    window.history.replaceState(null, '', `/chat/${key}`);
  }

  /**
   * Mounts the page fresh.
   *
   * Resets the testing module first so a test can mount twice — which is how a
   * browser reload is modelled: the component is destroyed and rebuilt while
   * `sessionStorage` survives, exactly as it does across a refresh.
   */
  async function mount(): Promise<void> {
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        HostedChatApi,
        HostedChatSessionStore,
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HostedChat);
    fixture.detectChanges();
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  /**
   * Opens the page as a first-time visitor.
   *
   * Only the endpoint's public face is fetched. Nothing is started — that is the
   * property most of this suite exists to hold.
   */
  async function open(agentName = 'Concierge'): Promise<void> {
    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName, available: true });
    await settle();
  }

  /** Types a message and submits it, answering the lazy start on the way. */
  async function sendFirst(text: string, agentName = 'Concierge'): Promise<void> {
    type(text);
    sendButton().click();
    await settle();

    http.expectOne(startUrl).flush({ conversationId, agentName });
    await settle();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  const composer = (): HTMLTextAreaElement =>
    host().querySelector('textarea') as HTMLTextAreaElement;

  const sendButton = (): HTMLButtonElement =>
    host().querySelector('button[type="submit"]') as HTMLButtonElement;

  const restartButton = (): HTMLButtonElement | null => host().querySelector('.chat-restart');

  const alertText = (): string => host().querySelector('[role="alert"]')?.textContent ?? '';

  function type(value: string): void {
    const input = composer();

    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  /** A turn the server accepted, for flushing a send. */
  function turn(customerText: string, reply = 'We close at six.') {
    return {
      customerMessage: {
        id: `c-${customerText}`,
        sender: 'Customer',
        text: customerText,
        sentAt: '2026-09-07T12:00:00Z',
      },
      reply: {
        id: `a-${customerText}`,
        sender: 'AiAgent',
        text: reply,
        sentAt: '2026-09-07T12:00:01Z',
      },
      handling: 'Ai',
    };
  }

  /**
   * A turn accepted while a person is handling the conversation.
   *
   * No reply, and that is a success rather than a failure: the message was
   * stored and a colleague will answer it.
   */
  function humanTurn(customerText: string) {
    return {
      customerMessage: {
        id: `c-${customerText}`,
        sender: 'Customer',
        text: customerText,
        sentAt: '2026-09-07T12:00:00Z',
      },
      reply: null,
      handling: 'Human',
    };
  }

  afterEach(() => {
    http.verify();
    window.history.replaceState(null, '', '/');
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  // ---- Opening the page creates nothing --------------------------------

  it('reads the endpoint and starts no conversation on arrival', async () => {
    // The Phase 15 defect this fixes: rendering the chat used to create a
    // conversation, so every page view, refresh and crawler left an empty one in
    // somebody's workspace. `http.verify()` in afterEach is what enforces it --
    // any request beyond the config read fails this test.
    await open('Night desk');

    expect(text()).toContain('Night desk');
    expect(text()).toContain('Ask anything to start the conversation.');
  });

  it('remembers nothing before the first message is sent', async () => {
    await open();

    expect(window.sessionStorage.length).toBe(0);
    expect(window.localStorage.length).toBe(0);
  });

  it('offers no new-chat action before there is a conversation to leave', async () => {
    await open();

    expect(restartButton()).toBeNull();
  });

  it('creates nothing when reopened with nothing remembered', async () => {
    // A reload before the first message. The page has no id to resume, so it
    // reads the config and stops -- exactly as a first arrival does.
    await open();
    await open();

    expect(window.sessionStorage.length).toBe(0);
  });

  // ---- The first send starts the conversation --------------------------

  it('starts a conversation on the first send and then sends the message', async () => {
    await open();

    type('When do you close?');
    sendButton().click();
    await settle();

    // The start comes first, and only because the visitor acted.
    http.expectOne(startUrl).flush({ conversationId, agentName: 'Concierge' });
    await settle();

    const request = http.expectOne(messagesUrl);

    expect(request.request.body.text).toBe('When do you close?');
    expect(request.request.body.clientMessageId).toBeTruthy();
    expect(Object.keys(request.request.body).sort()).toEqual(['clientMessageId', 'text']);

    request.flush(turn('When do you close?'));
    await settle();

    expect(text()).toContain('When do you close?');
    expect(text()).toContain('We close at six.');
    expect(composer().value).toBe('');
  });

  it('stores the conversation id only after the server confirms it', async () => {
    await open();

    type('Hello');
    sendButton().click();
    await settle();

    // Mid-flight: the start has been requested and not yet answered, so nothing
    // is remembered. A failed start must not leave a resume id pointing at a
    // conversation that was never created.
    expect(window.sessionStorage.getItem(resumeKey)).toBeNull();

    http.expectOne(startUrl).flush({ conversationId, agentName: 'Concierge' });
    await settle();

    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();
  });

  it('stores the conversation id and nothing else', async () => {
    // The one thing this page is allowed to keep. A conversation id is not a
    // credential: it appears in an administrator's list and grants nothing. The
    // secret that opens the conversation stays in an HttpOnly cookie.
    await open();
    await sendFirst('Hello');

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();

    expect(Object.keys(window.sessionStorage)).toEqual([resumeKey]);
    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);
    expect(window.localStorage.length).toBe(0);
  });

  it('starts only one conversation however many messages follow', async () => {
    await open();
    await sendFirst('First');

    http.expectOne(messagesUrl).flush(turn('First'));
    await settle();

    type('Second');
    sendButton().click();
    await settle();

    // No second start request: verify() would fail on one.
    http.expectOne(messagesUrl).flush(turn('Second'));
    await settle();

    expect(text()).toContain('First');
    expect(text()).toContain('Second');
  });

  it('scopes what it remembers to the endpoint it was opened at', async () => {
    // Two chats hosted on one origin -- a staging endpoint and a live one, or two
    // different businesses -- must never resume into each other's conversation.
    await open();
    await sendFirst('Hello');

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();

    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);
    expect(window.sessionStorage.getItem('omniai.webchat.conversation.zzzz')).toBeNull();
  });

  // ---- Resuming after a reload -----------------------------------------

  it('resumes the remembered conversation on reload', async () => {
    window.sessionStorage.setItem(resumeKey, conversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    // The transcript is read for the remembered id. The browser attaches the
    // matching HttpOnly credential by itself; nothing here holds one.
    http.expectOne(messagesUrl).flush({
      items: [
        { id: 'c1', sender: 'Customer', text: 'Earlier question', sentAt: '2026-09-07T12:00:00Z' },
        { id: 'a1', sender: 'AiAgent', text: 'Earlier answer', sentAt: '2026-09-07T12:00:01Z' },
      ],
      nextCursor: null,
    });
    await settle();

    expect(text()).toContain('Earlier question');
    expect(text()).toContain('Earlier answer');

    // And no conversation was started: verify() would fail on a start request.
    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);
  });

  it('continues the resumed conversation without starting another', async () => {
    window.sessionStorage.setItem(resumeKey, conversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    type('Still there?');
    sendButton().click();
    await settle();

    http.expectOne(messagesUrl).flush(turn('Still there?'));
    await settle();

    expect(text()).toContain('Still there?');
  });

  it('clears a stale conversation id and renders a fresh chat', async () => {
    // The remembered conversation has gone -- expired, or its credential is no
    // longer held. The visitor gets an empty chat and no explanation about server
    // retention, and nothing is created until they send something.
    window.sessionStorage.setItem(resumeKey, otherConversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    http
      .expectOne(`/api/public/webchat/conversations/${otherConversationId}/messages`)
      .flush(
        { title: 'Not found', detail: 'No such conversation.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(window.sessionStorage.getItem(resumeKey)).toBeNull();
    expect(text()).toContain('Ask anything to start the conversation.');
    expect(alertText()).toBe('');
  });

  it('an expired session leaves the visitor able to start again', async () => {
    // Expiry must not be a dead end. After the stale id is cleared, the next send
    // starts a new conversation exactly as a first arrival does.
    window.sessionStorage.setItem(resumeKey, otherConversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    http
      .expectOne(`/api/public/webchat/conversations/${otherConversationId}/messages`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    await settle();

    await sendFirst('Starting over');

    http.expectOne(messagesUrl).flush(turn('Starting over'));
    await settle();

    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);
    expect(text()).toContain('Starting over');
  });

  it('keeps the visitor talking when a resumed transcript cannot be re-read', async () => {
    // A transcript that failed to load must not stop somebody from asking their
    // question. They start with an empty view of a conversation the server still
    // has in full.
    window.sessionStorage.setItem(resumeKey, conversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    http.expectOne(messagesUrl).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    // The id is kept -- a 500 is not evidence the conversation has gone.
    expect(window.sessionStorage.getItem(resumeKey)).toBe(conversationId);
    expect(composer()).not.toBeNull();
  });

  // ---- Starting a new conversation deliberately ------------------------

  it('offers a new-chat action once a conversation exists', async () => {
    await open();
    await sendFirst('Hello');

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();

    expect(restartButton()).not.toBeNull();
  });

  it('starting a new chat creates nothing until the next message', async () => {
    await open();
    await sendFirst('Hello');

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();

    restartButton()!.click();
    await settle();

    // The remembered id is gone, the transcript is cleared, and no request was
    // made: verify() would fail on one.
    expect(window.sessionStorage.getItem(resumeKey)).toBeNull();
    expect(text()).toContain('Ask anything to start the conversation.');
    expect(text()).not.toContain('Hello');
    expect(restartButton()).toBeNull();
  });

  it('the next message after a new chat starts the next conversation', async () => {
    await open();
    await sendFirst('First conversation');

    http.expectOne(messagesUrl).flush(turn('First conversation'));
    await settle();

    restartButton()!.click();
    await settle();

    type('Second conversation');
    sendButton().click();
    await settle();

    // A fresh start, and then the message into the new conversation.
    http.expectOne(startUrl).flush({ conversationId: otherConversationId, agentName: 'Concierge' });
    await settle();

    http
      .expectOne(`/api/public/webchat/conversations/${otherConversationId}/messages`)
      .flush(turn('Second conversation'));
    await settle();

    expect(window.sessionStorage.getItem(resumeKey)).toBe(otherConversationId);
    expect(text()).toContain('Second conversation');
  });

  // ---- Rate limiting ---------------------------------------------------

  it('explains a rate limit in words a customer can act on', async () => {
    await open();
    await sendFirst('Are you there?');

    http.expectOne(messagesUrl).flush(
      { title: 'Too many requests', detail: 'Too many requests were made.' },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '45' },
      },
    );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    const shown = alertText();

    expect(shown).toContain('too quickly');
    expect(shown).toContain('45 seconds');

    // Nothing about quotas, budgets, workspaces or the counter store.
    for (const leak of ['quota', 'budget', 'redis', 'workspace', 'limit', 'counter']) {
      expect(shown.toLowerCase()).not.toContain(leak);
    }
  });

  it('rounds a long wait into minutes rather than counting seconds', async () => {
    await open();
    await sendFirst('Are you there?');

    http
      .expectOne(messagesUrl)
      .flush(
        {},
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '300' } },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(alertText()).toContain('5 minutes');
  });

  it('says the honest vague thing when no wait was given', async () => {
    await open();
    await sendFirst('Are you there?');

    http.expectOne(messagesUrl).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(alertText()).toContain('wait a moment');
    expect(alertText()).not.toContain('NaN');
    expect(alertText()).not.toContain('undefined');
  });

  it('ignores a malformed retry hint rather than rendering it', async () => {
    await open();
    await sendFirst('Are you there?');

    http.expectOne(messagesUrl).flush(
      {},
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': 'Wed, 21 Oct 2026 07:28:00 GMT' },
      },
    );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(alertText()).toContain('wait a moment');
    expect(alertText()).not.toContain('NaN');
  });

  it('keeps the unsent message and never retries a rate limit by itself', async () => {
    await open();
    await sendFirst('Important question');

    http.expectOne(messagesUrl).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    // Their words are still in the box, and nothing was sent again: verify()
    // would fail on any further request.
    expect(composer().value).toBe('Important question');
    expect(sendButton().disabled).toBe(false);
  });

  it('marks a rate limit as a warning rather than a failure', async () => {
    // Nothing is broken and nothing was lost, so it must not read like an error.
    await open();
    await sendFirst('Are you there?');

    http.expectOne(messagesUrl).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    const alert = host().querySelector('[role="alert"]');

    expect(alert?.classList.contains('alert-warning')).toBe(true);
    expect(alert?.classList.contains('alert-danger')).toBe(false);
  });

  it('handles a refused start without losing the message', async () => {
    // The first send is two requests, and the start is the one that can be rate
    // limited. The visitor's words must survive that too.
    await open();

    type('My first question');
    sendButton().click();
    await settle();

    http
      .expectOne(startUrl)
      .flush(
        {},
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } },
      );
    await settle();

    expect(alertText()).toContain('too quickly');
    expect(composer().value).toBe('My first question');

    // Nothing was remembered, because nothing was created.
    expect(window.sessionStorage.getItem(resumeKey)).toBeNull();
  });

  // ---- Provider and protection-store failures --------------------------

  it.each([
    [503, 'Chat unavailable'],
    [504, 'AI generation timed out'],
    [502, 'AI generation failed'],
  ])('shows a failure for %i without inventing a reply or retrying', async (status, title) => {
    await open();
    await sendFirst('Are you there?');

    http
      .expectOne(messagesUrl)
      .flush({ title, detail: 'The reply could not be produced.' }, { status, statusText: title });
    await settle();

    // The page re-reads what the server actually stored.
    http.expectOne(messagesUrl).flush({
      items: [
        { id: 'c1', sender: 'Customer', text: 'Are you there?', sentAt: '2026-09-07T12:00:00Z' },
      ],
      nextCursor: null,
    });
    await settle();

    expect(text()).toContain('Are you there?');
    expect(text()).not.toContain('undefined');
  });

  it('reports a provider failure in words a customer can act on', async () => {
    // The shared copy is written for administrators -- "AI preview is currently
    // unavailable because no provider is configured" tells somebody who came to
    // ask about a delivery about this platform's deployment.
    await open();
    await sendFirst('Are you there?');

    http
      .expectOne(messagesUrl)
      .flush(
        { title: 'Chat unavailable', detail: 'No AI runtime is configured on this deployment.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    const shown = alertText();

    expect(shown).toContain('no answer came back just now');
    expect(shown).toContain('try sending it again');

    for (const leak of ['provider', 'preview', 'configured', 'runtime', 'deployment', 'redis']) {
      expect(shown.toLowerCase()).not.toContain(leak);
    }
  });

  it('says the same generic thing when the protection store is unavailable', async () => {
    // A visitor must not be able to tell an unreachable counter store from an
    // unconfigured provider. Both are "no answer right now".
    await open();
    await sendFirst('Are you there?');

    http
      .expectOne(messagesUrl)
      .flush(
        { title: 'Chat unavailable', detail: 'This chat is temporarily unavailable.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(alertText()).toContain('no answer came back just now');
  });

  it('keeps the visitor’s words in the box when a send fails', async () => {
    await open();
    await sendFirst('Important question');

    http
      .expectOne(messagesUrl)
      .flush(
        { title: 'Chat unavailable', detail: 'No AI runtime is configured.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(composer().value).toBe('Important question');
  });

  // ---- Rendering and safety --------------------------------------------

  it('renders script-like content as text and never as markup', async () => {
    await open();

    const payload = '<script>alert(1)</script>';

    await sendFirst(payload);

    http.expectOne(messagesUrl).flush({
      customerMessage: {
        id: 'c1',
        sender: 'Customer',
        text: payload,
        sentAt: '2026-09-07T12:00:00Z',
      },
      reply: {
        id: 'a1',
        sender: 'AiAgent',
        text: '<img src=x onerror=alert(2)>',
        sentAt: '2026-09-07T12:00:01Z',
      },
    });
    await settle();

    expect(text()).toContain(payload);
    expect(host().querySelector('script')).toBeNull();
    expect(host().querySelector('img')).toBeNull();
  });

  it('does not send twice while a send is in flight', async () => {
    await open();
    await sendFirst('Hello');

    expect(sendButton().disabled).toBe(true);

    sendButton().click();
    await settle();

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();
  });

  it('does not put the conversation id in the address bar', async () => {
    await open();
    await sendFirst('Hello');

    http.expectOne(messagesUrl).flush(turn('Hello'));
    await settle();

    expect(window.location.pathname).toBe(`/chat/${publicKey}`);
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('');
  });

  it('explains a bad link without mentioning a workspace', async () => {
    openAt(publicKey);
    await mount();

    http
      .expectOne(configUrl)
      .flush(
        { title: 'Not found', detail: 'No such chat.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(text()).toContain('not valid');
    expect(text().toLowerCase()).not.toContain('workspace');
  });

  it('says the chat is unavailable without saying why', async () => {
    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: false });
    await settle();

    expect(text()).toContain('not available right now');
    expect(host().querySelector('textarea')).toBeNull();
  });

  it('explains an unknown link rather than showing an empty chat', async () => {
    openAt(publicKey);
    await mount();

    http
      .expectOne(configUrl)
      .flush(
        { title: 'Not found', detail: 'No such chat.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(text()).toContain('could not be opened');
    expect(host().querySelector('textarea')).toBeNull();
  });

  it('labels the composer and offers a submit button', async () => {
    await open();

    const label = host().querySelector('label[for="chat-message"]');

    expect(label?.textContent?.trim()).toBe('Your message');
    expect(composer().id).toBe('chat-message');
    expect(sendButton().type).toBe('submit');
  });

  it('announces the transcript and its status messages', async () => {
    // A rate limit or a failure that only appeared visually would leave somebody
    // typing into a box that is quietly refusing them.
    await open();

    expect(host().querySelector('[aria-live="polite"]')).not.toBeNull();

    await sendFirst('Are you there?');

    http.expectOne(messagesUrl).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(host().querySelector('[role="alert"]')).not.toBeNull();
  });

  // ---- A person takes over ---------------------------------------------

  it('says a person will reply, and shows no fake typing indicator', async () => {
    // A turn with no reply is not an error. Showing one would tell a visitor
    // their message did not arrive, when it did — and a typing indicator when
    // nobody may be at their desk is a promise the platform cannot keep.
    await open();
    await sendFirst('Where is my order?');

    http.expectOne(messagesUrl).flush(humanTurn('Where is my order?'));
    await settle();

    expect(text()).toContain('A support team member will reply here.');
    expect(text()).not.toContain('is typing');
    expect(alertText()).toBe('');

    // The visitor's own message is on screen, and no reply was invented.
    expect(text()).toContain('Where is my order?');
  });

  it('labels a reply from a person Support, never an employee', async () => {
    // The disclosure boundary as the visitor sees it. The server sends a role
    // and no identity, and this asserts the client renders the role.
    window.sessionStorage.setItem(resumeKey, conversationId);

    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName: 'Concierge', available: true });
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [
        { id: 'c1', sender: 'Customer', text: 'Where is it?', sentAt: '2026-09-07T12:00:00Z' },
        {
          id: 'h1',
          sender: 'HumanAgent',
          text: 'We refunded it this morning.',
          sentAt: '2026-09-07T12:01:00Z',
        },
      ],
      nextCursor: null,

      // The server sends the mode with every transcript read, which is how a
      // visitor learns a person has taken over without waiting for them to
      // reply. A fixture without it would be testing a response shape the API
      // does not produce.
      handling: 'Human',
    });
    await settle();

    expect(text()).toContain('Support');
    expect(text()).toContain('We refunded it this morning.');

    // Nothing resembling a colleague's address reached the page.
    expect(text()).not.toContain('@');

    // A resumed conversation that already has a human reply is known to be in
    // human mode, so the visitor is told rather than left waiting on an AI.
    expect(text()).toContain('A support team member will reply here.');
  });
});
