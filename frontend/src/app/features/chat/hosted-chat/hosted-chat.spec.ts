import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HostedChatApi } from '../hosted-chat-api';
import { HostedChat } from './hosted-chat';

describe('HostedChat', () => {
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';
  const conversationId = '11111111-1111-7111-8111-111111111111';

  const configUrl = `/api/public/webchat/${publicKey}`;
  const startUrl = `/api/public/webchat/${publicKey}/conversations`;
  const messagesUrl = `/api/public/webchat/conversations/${conversationId}/messages`;

  let fixture: ComponentFixture<HostedChat>;
  let http: HttpTestingController;

  /** Puts the page at a hosted chat address, as a visitor's browser would. */
  function openAt(key: string): void {
    window.history.replaceState(null, '', `/chat/${key}`);
  }

  async function mount(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), HostedChatApi],
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

  /** Opens the page all the way to a live conversation. */
  async function open(agentName = 'Concierge'): Promise<void> {
    openAt(publicKey);
    await mount();

    http.expectOne(configUrl).flush({ agentName, available: true });
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

  function type(value: string): void {
    const input = composer();

    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    window.history.replaceState(null, '', '/');
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  it('reads the endpoint and starts a conversation on arrival', async () => {
    await open('Night desk');

    expect(text()).toContain('Night desk');
    expect(text()).toContain('Ask anything to start the conversation.');
  });

  it('never stores anything in browser storage', async () => {
    // The conversation credential is an HttpOnly cookie the browser holds and
    // this page cannot read. Nothing here may write a copy of anything.
    await open();

    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(0);
  });

  it('does not put the conversation id in the address bar', async () => {
    await open();

    expect(window.location.pathname).toBe(`/chat/${publicKey}`);
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('');
  });

  it('sends a message and renders the reply', async () => {
    await open('Concierge');

    type('When do you close?');
    sendButton().click();
    await settle();

    const request = http.expectOne(messagesUrl);

    expect(request.request.body.text).toBe('When do you close?');

    // A fresh idempotency key per message, so a retry is recognised rather than
    // answered again.
    expect(request.request.body.clientMessageId).toBeTruthy();

    // The server owns everything else. A visitor cannot claim a sender.
    expect(Object.keys(request.request.body).sort()).toEqual(['clientMessageId', 'text']);

    request.flush({
      customerMessage: {
        id: 'c1',
        sender: 'Customer',
        text: 'When do you close?',
        sentAt: '2026-09-07T12:00:00Z',
      },
      reply: {
        id: 'a1',
        sender: 'AiAgent',
        text: 'We close at six.',
        sentAt: '2026-09-07T12:00:01Z',
      },
    });
    await settle();

    expect(text()).toContain('When do you close?');
    expect(text()).toContain('We close at six.');
    expect(composer().value).toBe('');
  });

  it('renders script-like content as text and never as markup', async () => {
    // The reason this page uses interpolation and never innerHTML.
    await open();

    const payload = '<script>alert(1)</script>';

    type(payload);
    sendButton().click();
    await settle();

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

    type('Hello');
    sendButton().click();
    await settle();

    // Disabled while sending, and a second click issues nothing -- the double
    // click is stopped here as well as at the server.
    expect(sendButton().disabled).toBe(true);

    sendButton().click();
    await settle();

    const request = http.expectOne(messagesUrl);

    request.flush({
      customerMessage: {
        id: 'c1',
        sender: 'Customer',
        text: 'Hello',
        sentAt: '2026-09-07T12:00:00Z',
      },
      reply: { id: 'a1', sender: 'AiAgent', text: 'Hi.', sentAt: '2026-09-07T12:00:01Z' },
    });
    await settle();
  });

  it.each([
    [503, 'Chat unavailable', 'AI generation unavailable'],
    [504, 'AI generation timed out', 'AI generation timed out'],
    [502, 'AI generation failed', 'AI generation failed'],
  ])(
    'shows a failure for %i without inventing a reply and without retrying',
    async (status, title) => {
      await open();

      type('Are you there?');
      sendButton().click();
      await settle();

      http
        .expectOne(messagesUrl)
        .flush(
          { title, detail: 'The reply could not be produced.' },
          { status, statusText: title },
        );
      await settle();

      // The page re-reads what the server actually stored, which is the customer
      // message alone.
      http.expectOne(messagesUrl).flush({
        items: [
          {
            id: 'c1',
            sender: 'Customer',
            text: 'Are you there?',
            sentAt: '2026-09-07T12:00:00Z',
          },
        ],
        nextCursor: null,
      });
      await settle();

      expect(text()).toContain('Are you there?');

      // No fabricated answer, and nothing retried on its own: verify() would fail
      // on any further request.
      expect(text()).not.toContain('undefined');
    },
  );

  it('reports a failure in words a customer can act on', async () => {
    // The shared copy is written for administrators -- "AI preview is currently
    // unavailable because no provider is configured" tells somebody who came to
    // ask about a delivery about this platform's deployment. It is also internal
    // operational detail a visitor has no business being given.
    await open();

    type('Are you there?');
    sendButton().click();
    await settle();

    http
      .expectOne(messagesUrl)
      .flush(
        { title: 'Chat unavailable', detail: 'No AI runtime is configured on this deployment.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    const shown = host().querySelector('[role="alert"]')?.textContent ?? '';

    expect(shown).toContain('no answer came back just now');
    expect(shown).toContain('try sending it again');

    // Nothing about providers, runtimes, configuration or previews.
    for (const leak of ['provider', 'preview', 'configured', 'runtime', 'deployment']) {
      expect(shown.toLowerCase()).not.toContain(leak);
    }
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

  it('keeps the visitor’s words in the box when a send fails', async () => {
    await open();

    type('Important question');
    sendButton().click();
    await settle();

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

  it('says the chat is unavailable without saying why', async () => {
    // A draft endpoint, a withdrawn one and a disabled agent are one state to a
    // visitor. The page must not become a status page for the workspace.
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
    // Keyboard-reachable and named, because this page is somebody's only way to
    // talk to the business.
    await open();

    const label = host().querySelector('label[for="chat-message"]');

    expect(label?.textContent?.trim()).toBe('Your message');
    expect(composer().id).toBe('chat-message');
    expect(sendButton().type).toBe('submit');
  });
});
