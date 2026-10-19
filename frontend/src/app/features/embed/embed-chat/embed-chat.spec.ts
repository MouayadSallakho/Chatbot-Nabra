import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PublicChatRealtime } from '@app/core/realtime/public-chat-realtime';
import { FakePublicChatRealtime } from '@app/testing/test-doubles';
import { EmbedBridge } from '../embed-bridge';
import { EmbedChatApi } from '../embed-chat-api';
import { EmbedSessionStore } from '../embed-session';
import { EmbedChat } from './embed-chat';

describe('EmbedChat', () => {
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';
  const parentOrigin = 'https://shop.example.com';
  const conversationId = '11111111-1111-7111-8111-111111111111';
  const credential = 'ZmFrZS1lbWJlZC1jcmVkZW50aWFs';
  const configUrl = `/api/public/embed/${publicKey}`;
  const messagesUrl = `/api/public/embed/conversations/${conversationId}/messages`;

  let fixture: ComponentFixture<EmbedChat>;
  let http: HttpTestingController;
  let parent: { postMessage: ReturnType<typeof vi.fn> };
  let realParent: Window;
  let path: string;

  const config = (overrides: Record<string, unknown> = {}) => ({
    agentName: 'Concierge',
    isAvailable: true,
    greeting: null,
    accentColor: '#2563eb',
    launcherPosition: 'Right',
    theme: 'System',
    ...overrides,
  });

  const message = (id: string, sender: string, text: string) => ({
    id,
    sender,
    text,
    sentAt: '2026-09-13T10:00:00Z',
  });

  function deliverInit(): void {
    const event = new MessageEvent('message', {
      data: { type: 'omniai:v1:init' },
      origin: parentOrigin,
    });

    Object.defineProperty(event, 'source', { value: parent });

    window.dispatchEvent(event);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  /** Mounts the widget and completes the handshake, leaving the config pending. */
  async function mount(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        EmbedChatApi,
        EmbedSessionStore,
        EmbedBridge,
        { provide: PublicChatRealtime, useClass: FakePublicChatRealtime },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmbedChat);
    fixture.detectChanges();

    // The loader's handshake. Without it the widget asks the server nothing,
    // because it does not yet know which website it is on.
    deliverInit();
    await settle();
  }

  /** Mounts, hands over a configuration, and opens the panel. */
  async function open(overrides: Record<string, unknown> = {}): Promise<void> {
    await mount();

    http.expectOne(configUrl).flush(config(overrides));
    await settle();

    launcher()!.click();
    await settle();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';
  const launcher = (): HTMLButtonElement | null =>
    host().querySelector('[data-testid="chat-launcher"]');
  const composer = (): HTMLTextAreaElement | null =>
    host().querySelector('[data-testid="chat-input"]');

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  async function type(value: string): Promise<void> {
    const box = composer()!;

    box.value = value;
    box.dispatchEvent(new Event('input'));
    await settle();
  }

  beforeEach(() => {
    path = window.location.pathname;
    window.history.replaceState({}, '', `/embed/${publicKey}`);

    parent = { postMessage: vi.fn() };
    realParent = window.parent;
    Object.defineProperty(window, 'parent', { value: parent, configurable: true });

    window.sessionStorage.clear();
  });

  afterEach(() => {
    http.verify();
    Object.defineProperty(window, 'parent', { value: realParent, configurable: true });
    window.history.replaceState({}, '', path);
    window.sessionStorage.clear();
    document.documentElement.removeAttribute('data-theme');

    for (const element of [document.documentElement, document.body]) {
      element.style.removeProperty('color-scheme');
      element.style.removeProperty('background-color');
    }
  });

  it('asks the server nothing until the browser has told it which website it is on', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        EmbedChatApi,
        EmbedSessionStore,
        EmbedBridge,
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmbedChat);
    fixture.detectChanges();
    await settle();

    // No handshake has happened, so there is no origin to name and nothing to
    // ask about. verify() in afterEach proves no request was made.
    expect(launcher()).toBeNull();
  });

  it('names the website the browser reported, on every request', async () => {
    await mount();

    const request = http.expectOne(configUrl);

    expect(request.request.headers.get('X-OmniAI-Embed-Origin')).toBe(parentOrigin);

    request.flush(config());
    await settle();

    expect(launcher()).not.toBeNull();
  });

  it('tells the page which corner it wants, and nothing else', async () => {
    await mount();

    http.expectOne(configUrl).flush(config({ launcherPosition: 'Left' }));
    await settle();

    expect(parent.postMessage).toHaveBeenCalledWith(
      { type: 'omniai:v1:ready', position: 'Left' },
      parentOrigin,
    );
  });

  it('says nothing to the page at all when it may not run there', async () => {
    await mount();

    // The server answers 404 for an unknown key, a website nobody approved, and
    // a website whose approval was withdrawn. All three look the same here.
    http.expectOne(configUrl).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(launcher()).toBeNull();
    expect(parent.postMessage).not.toHaveBeenCalled();
  });

  it('shows no launcher for a chat that is switched off', async () => {
    await mount();

    http.expectOne(configUrl).flush(config({ isAvailable: false }));
    await settle();

    expect(launcher()).toBeNull();
  });

  it('sends from an ordinary button, because the sandbox withholds allow-forms', async () => {
    // A real form submission is blocked by the browser in this frame, so a
    // type="submit" button would silently do nothing. Found while checking the
    // sandbox against what the composer actually needs.
    await open();

    const send = button('Send')!;

    expect(send.getAttribute('type')).toBe('button');
    // No form at all: nothing in the panel could ever be submitted.
    expect(host().querySelector('form')).toBeNull();
  });

  it('creates no conversation on load, and none on opening the panel', async () => {
    await open();

    // Only the configuration read has happened. verify() in afterEach is what
    // proves nothing was created: a start would be an unexpected POST.
    expect(composer()).not.toBeNull();
    expect(parent.postMessage).toHaveBeenCalledWith({ type: 'omniai:v1:open' }, parentOrigin);
  });

  it('starts the conversation on the first real send, and then reuses it', async () => {
    await open();
    await type('Where is my order?');

    button('Send')!.click();
    await settle();

    const start = http.expectOne(`${configUrl}/conversations`);

    expect(start.request.headers.get('X-OmniAI-Embed-Origin')).toBe(parentOrigin);

    start.flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    const send = http.expectOne(messagesUrl);

    expect(send.request.headers.get('X-OmniAI-Embed-Session')).toBe(credential);
    expect(send.request.body.text).toBe('Where is my order?');
    expect(send.request.body.clientMessageId).toBeTruthy();

    send.flush({
      customerMessage: message('m1', 'Customer', 'Where is my order?'),
      reply: message('m2', 'AiAgent', 'It ships tomorrow.'),
    });
    await settle();

    expect(text()).toContain('It ships tomorrow.');

    // A second message reuses the conversation rather than starting another.
    await type('Thank you.');
    button('Send')!.click();
    await settle();

    http.expectOne(messagesUrl).flush({
      customerMessage: message('m3', 'Customer', 'Thank you.'),
      reply: message('m4', 'AiAgent', 'You are welcome.'),
    });
    await settle();

    expect(text()).toContain('You are welcome.');
  });

  it('keeps the credential out of the page and out of the address', async () => {
    await open();
    await type('Hello');

    button('Send')!.click();
    await settle();

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    http.expectOne(messagesUrl).flush({
      customerMessage: message('m1', 'Customer', 'Hello'),
      reply: message('m2', 'AiAgent', 'Hi.'),
    });
    await settle();

    expect(JSON.stringify(parent.postMessage.mock.calls)).not.toContain(credential);
    expect(window.location.href).not.toContain(credential);
    expect(window.localStorage.length).toBe(0);

    // It is in this iframe's own sessionStorage, and only there.
    expect(JSON.stringify(window.sessionStorage)).toContain(credential);
  });

  it('renders what a customer typed as text, never as markup', async () => {
    await open();
    await type('<img src=x onerror="alert(1)">');

    button('Send')!.click();
    await settle();

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    http.expectOne(messagesUrl).flush({
      customerMessage: message('m1', 'Customer', '<img src=x onerror="alert(1)">'),
      reply: message('m2', 'AiAgent', '<b>bold</b>'),
    });
    await settle();

    expect(host().querySelector('[data-testid="chat-transcript"] img')).toBeNull();
    expect(host().querySelector('[data-testid="chat-transcript"] b')).toBeNull();
    expect(text()).toContain('<img src=x onerror="alert(1)">');
    expect(text()).toContain('<b>bold</b>');
  });

  it('shows the greeting as the workspace text it is, not as something the AI said', async () => {
    await open({ greeting: 'Hi — ask us anything.' });

    const greeting = host().querySelector('[data-testid="chat-greeting"]');

    expect(greeting?.textContent).toContain('Hi — ask us anything.');
    expect(host().querySelector('[data-testid="chat-message"]')).toBeNull();
  });

  it('resumes the conversation it remembers, credential and all', async () => {
    window.sessionStorage.setItem(
      `omniai.embed.session.${publicKey}`,
      JSON.stringify({ conversationId, credential }),
    );

    await mount();

    http.expectOne(configUrl).flush(config());
    await settle();

    const resumed = http.expectOne(messagesUrl);

    expect(resumed.request.headers.get('X-OmniAI-Embed-Session')).toBe(credential);

    resumed.flush({ items: [message('m1', 'AiAgent', 'Earlier answer.')], nextCursor: null });
    await settle();

    launcher()!.click();
    await settle();

    expect(text()).toContain('Earlier answer.');
  });

  it('forgets a conversation the server no longer admits, and starts clean', async () => {
    window.sessionStorage.setItem(
      `omniai.embed.session.${publicKey}`,
      JSON.stringify({ conversationId, credential }),
    );

    await mount();

    http.expectOne(configUrl).flush(config());
    await settle();

    // An expired session, or a website whose approval was withdrawn since. Both
    // leave the visitor with a fresh empty widget rather than an error.
    http.expectOne(messagesUrl).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(window.sessionStorage.getItem(`omniai.embed.session.${publicKey}`)).toBeNull();

    launcher()!.click();
    await settle();

    expect(composer()).not.toBeNull();
  });

  it('keeps the visitor words when a send fails, and retries nothing', async () => {
    await open();
    await type('Hello');

    button('Send')!.click();
    await settle();

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    http
      .expectOne(messagesUrl)
      .flush(
        { title: 'Too many requests', detail: 'Slow down.' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } },
      );
    await settle();

    // The failed turn re-reads what the server stored, then stops. Nothing is
    // sent again: being told to wait is not licence to poll.
    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(composer()!.value).toBe('Hello');
    expect(text()).toContain('too quickly');
    expect(text()).toContain('30 seconds');
  });

  it('tells a visitor nothing about the deployment when generation fails', async () => {
    await open();
    await type('Hello');

    button('Send')!.click();
    await settle();

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    http
      .expectOne(messagesUrl)
      .flush(
        { type: 'https://omniai.dev/problems/ai-provider-unavailable', title: 'Unavailable' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(text()).toContain('no answer came back');
    expect(text()).not.toContain('provider');
    expect(text()).not.toContain('configured');
    // Phase 34: a 503 may mean nothing was stored, so it never claims receipt.
    expect(text()).not.toContain('was received');
  });

  it('stamps the theme the workspace chose, and leaves System to the visitor', async () => {
    await open({ theme: 'Dark' });

    expect(document.documentElement.dataset['theme']).toBe('dark');

    TestBed.resetTestingModule();

    await open({ theme: 'System' });

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('makes the document transparent only once it knows it is embedded', async () => {
    // A dark canvas painted by the browser behind a launcher-sized frame is a
    // black rectangle over the customer's own design, so the colour scheme is
    // neutralised as well as the background.
    await open();

    expect(document.documentElement.style.backgroundColor).toBe('transparent');
    expect(document.documentElement.style.colorScheme).toBe('normal');
    expect(document.body.style.backgroundColor).toBe('transparent');
  });

  // ---- Phase 36.9: keyboard, names and the accent ---------------------------

  it('names the launcher by who answers, and moves focus in and back out', async () => {
    await mount();

    http.expectOne(configUrl).flush(config());
    await settle();

    expect(launcher()!.getAttribute('aria-label')).toBe('Chat with Concierge');

    launcher()!.click();
    await settle();

    // Opening puts the cursor in the message box.
    expect(document.activeElement).toBe(composer());

    // Escape closes, tells the page, and returns focus to the launcher.
    host().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(composer()).toBeNull();
    expect(parent.postMessage).toHaveBeenCalledWith({ type: 'omniai:v1:close' }, parentOrigin);
    expect(document.activeElement).toBe(launcher());
  });

  it('checks the accent before painting with it', async () => {
    await mount();

    // A value that could not have come from the server never reaches a style.
    http.expectOne(configUrl).flush(config({ accentColor: 'red;background:url(x)' }));
    await settle();

    const style = host().style;

    expect(style.getPropertyValue('--chat-accent')).toBe('#2563eb');
    expect(style.getPropertyValue('--chat-on-accent')).toBe('#ffffff');
    expect(style.cssText).not.toContain('url(');
  });

  it('starts listening before the first turn is attempted', async () => {
    await open();
    await type('Hello');

    button('Send')!.click();
    await settle();

    const realtime = TestBed.inject(PublicChatRealtime) as unknown as FakePublicChatRealtime;

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    expect(realtime.listens.length).toBe(1);

    http.expectOne(messagesUrl).flush({
      customerMessage: message('c1', 'Customer', 'Hello'),
      reply: message('a1', 'AiAgent', 'Hi'),
      handling: 'Ai',
    });
    await settle();
  });

  it('retires a failure notice once a person has answered over realtime', async () => {
    await open();
    await type('Hello');

    button('Send')!.click();
    await settle();

    http
      .expectOne(`${configUrl}/conversations`)
      .flush({ conversationId, agentName: 'Concierge', credential });
    await settle();

    // The AI turn fails honestly; the page re-reads what was stored.
    http.expectOne(messagesUrl).flush({}, { status: 503, statusText: 'Service Unavailable' });
    await settle();
    http.expectOne(messagesUrl).flush({
      items: [message('c1', 'Customer', 'Hello')],
      nextCursor: null,
      handling: 'Ai',
    });
    await settle();

    expect(host().querySelector('[data-testid="chat-error"]')).not.toBeNull();

    // A colleague takes over and replies: realtime says something changed.
    const realtime = TestBed.inject(PublicChatRealtime) as unknown as FakePublicChatRealtime;

    realtime.listens.at(-1)!.onChanged();
    await settle();
    http.expectOne(messagesUrl).flush({
      items: [message('c1', 'Customer', 'Hello'), message('h1', 'HumanAgent', 'I can help.')],
      nextCursor: null,
      handling: 'Human',
    });
    await settle();

    expect(host().querySelector('[data-testid="chat-error"]')).toBeNull();
    expect(text()).toContain('A support team member will reply here.');
    expect(text()).toContain('A person from the support team joined');
  });
});
