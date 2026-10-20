// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';

/**
 * The loader that runs on a customer's website.
 *
 * <strong>Read from the file the API actually ships.</strong> It is compiled into
 * the Api assembly as an embedded resource and served at `/widget/v1.js`; a copy
 * kept beside this test would be a second loader that could quietly diverge from
 * the one customers have pasted into their pages.
 */
const SOURCE: string = readFileSync('../backend/src/OmniAI.Api/Widget/v1.js', 'utf8');

describe('widget loader v1', () => {
  const origin = 'https://omniai.test';
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';

  let realCurrentScript: PropertyDescriptor | undefined;

  /**
   * Runs the loader as the browser would, with a script element it can find.
   *
   * `document.currentScript` is how the loader reads its own configuration and
   * derives the OmniAI origin from its own `src`. Nothing is configured any other
   * way, which is what makes the same file work on any deployment.
   */
  function run(options: { key?: string | null; src?: string } = {}): void {
    const script = document.createElement('script');

    script.src = options.src ?? `${origin}/widget/v1.js`;

    if (options.key !== null) {
      script.setAttribute('data-omniai-key', options.key ?? publicKey);
    }

    Object.defineProperty(document, 'currentScript', { value: script, configurable: true });

    // The loader is an IIFE, exactly as served. Evaluating the shipped bytes is
    // the point: a rewritten copy would prove nothing about what customers run.
    new Function(SOURCE)();
  }

  const frame = (): HTMLIFrameElement | null => document.querySelector('iframe');

  /** Delivers a message as if it came from inside the widget's own frame. */
  function fromWidget(data: unknown, messageOrigin = origin, source?: unknown): void {
    const event = new MessageEvent('message', { data, origin: messageOrigin });

    Object.defineProperty(event, 'source', { value: source ?? frame()!.contentWindow });

    window.dispatchEvent(event);
  }

  function posted(): { postMessage: ReturnType<typeof vi.fn> } {
    return frame()!.contentWindow as unknown as { postMessage: ReturnType<typeof vi.fn> };
  }

  /**
   * Retires every frame a test created.
   *
   * The loader is written to live for the lifetime of a page: it never removes
   * its `message` and `resize` listeners, because on a real website there is
   * nothing to remove them for. In one long-lived test document that means an
   * earlier test's loader is still listening while a later one runs, and would
   * try to talk to a frame that has been taken out of the document. Clearing
   * `contentWindow` first is what the loader's own guard then sees.
   */
  function retireFrames(): void {
    for (const created of document.querySelectorAll('iframe')) {
      Object.defineProperty(created, 'contentWindow', { value: null, configurable: true });
    }

    document.body.replaceChildren();
  }

  beforeEach(() => {
    realCurrentScript = Object.getOwnPropertyDescriptor(Document.prototype, 'currentScript');
    retireFrames();

    // One widget per page is enforced with a window flag, so each test starts
    // from a page that has not loaded one.
    delete (window as unknown as Record<string, unknown>)['__omniaiWidgetLoaded'];
  });

  afterEach(() => {
    vi.useRealTimers();
    retireFrames();

    // `run` shadows the getter with an own property of this document; removing
    // it gives the prototype's back. The runner shares one document between
    // specs in a worker, so a script left here would outlive this file.
    delete (document as unknown as Record<string, unknown>)['currentScript'];

    if (realCurrentScript) {
      Object.defineProperty(Document.prototype, 'currentScript', realCurrentScript);
    }
  });

  it('creates exactly one frame, pointed at the widget for this key', () => {
    run();

    expect(document.querySelectorAll('iframe')).toHaveLength(1);
    expect(frame()!.src).toBe(`${origin}/embed/${publicKey}`);
  });

  it('derives the OmniAI origin from its own address, never from configuration', () => {
    run({ src: 'https://chat.acme-omniai.example/widget/v1.js' });

    expect(frame()!.src).toBe(`https://chat.acme-omniai.example/embed/${publicKey}`);
  });

  it('escapes the key it puts in the address', () => {
    run({ key: 'a/b?c#d' });

    expect(frame()!.src).toBe(`${origin}/embed/a%2Fb%3Fc%23d`);
  });

  it('does nothing at all without a key', () => {
    run({ key: null });

    expect(frame()).toBeNull();
  });

  it('loads once however many times a tag manager runs it', () => {
    run();
    run();
    run();

    expect(document.querySelectorAll('iframe')).toHaveLength(1);
  });

  it('grants the frame exactly two sandbox capabilities', () => {
    run();

    const granted = frame()!.getAttribute('sandbox')!.split(' ').filter(Boolean).sort();

    // The minimum the widget actually needs: scripts, because it is an Angular
    // application; and same-origin, because it stays on the OmniAI origin, which
    // is what lets it call its own API and use its own sessionStorage.
    //
    // allow-same-origin grants the iframe nothing over this page -- the two are
    // different origins, so the browser keeps them apart regardless.
    expect(granted).toEqual(['allow-same-origin', 'allow-scripts']);
  });

  it('withholds every capability a chat panel does not need', () => {
    run();

    const sandbox = frame()!.getAttribute('sandbox')!;

    // Named one by one rather than asserted as a set, so that a future edit that
    // adds one has to delete the line that says why it must not exist.
    //
    // allow-forms: the composer never submits a form. The Send button is an
    //   ordinary button and Enter is handled as a keystroke, so nothing needs the
    //   capability -- and a widget that could submit forms is a widget that could
    //   be used to post somewhere on a visitor's behalf.
    // allow-popups / allow-top-navigation: a widget that could navigate or open
    //   windows from the host page could be used to phish that site's own
    //   visitors.
    // allow-modals: a chat panel has no business interrupting the page.
    // allow-downloads / allow-pointer-lock / allow-presentation: nothing here
    //   downloads, captures the cursor, or casts.
    for (const withheld of [
      'allow-forms',
      'allow-popups',
      'allow-popups-to-escape-sandbox',
      'allow-top-navigation',
      'allow-top-navigation-by-user-activation',
      'allow-modals',
      'allow-downloads',
      'allow-pointer-lock',
      'allow-presentation',
      'allow-orientation-lock',
      'allow-storage-access-by-user-activation',
    ]) {
      expect(sandbox).not.toContain(withheld);
    }
  });

  it('asks for no device permission of any kind', () => {
    run();

    // Camera, microphone and geolocation are denied by default in a cross-origin
    // frame. Saying so explicitly means a future default cannot quietly grant one.
    expect(frame()!.getAttribute('allow')).toBe('');
  });

  it('sends the origin as the referrer and never the page path', () => {
    run();

    // The host page's path can carry an order number, a search term or a customer
    // identifier, and none of that is OmniAI's business.
    expect(frame()!.getAttribute('referrerpolicy')).toBe('strict-origin');
  });

  it('announces itself with a real title', () => {
    run();

    // A frame announced as "iframe" is a frame a screen-reader user has no reason
    // to enter.
    expect(frame()!.title).toBe('Chat');
  });

  it('greets the widget at its exact origin, repeatedly until it answers', async () => {
    vi.useFakeTimers();

    run();

    const contentWindow = posted();

    contentWindow.postMessage = vi.fn();

    frame()!.dispatchEvent(new Event('load'));

    expect(contentWindow.postMessage).toHaveBeenCalledWith({ type: 'omniai:v1:init' }, origin);

    // The frame's load event and the widget's start-up are not ordered with
    // respect to each other, so a single greeting could land on nobody.
    await vi.advanceTimersByTimeAsync(600);

    expect(contentWindow.postMessage.mock.calls.length).toBeGreaterThan(1);

    const calls = contentWindow.postMessage.mock.calls.length;

    fromWidget({ type: 'omniai:v1:ready', position: 'Right' });
    contentWindow.postMessage.mockClear();

    await vi.advanceTimersByTimeAsync(2_000);

    // Answered, so it stops. The one message after READY is the viewport reply.
    expect(contentWindow.postMessage.mock.calls.length).toBeLessThan(calls);
  });

  it('never addresses the frame with a wildcard', async () => {
    vi.useFakeTimers();

    run();

    const contentWindow = posted();

    contentWindow.postMessage = vi.fn();

    frame()!.dispatchEvent(new Event('load'));
    fromWidget({ type: 'omniai:v1:ready', position: 'Left' });
    window.dispatchEvent(new Event('resize'));

    await vi.advanceTimersByTimeAsync(1_000);

    expect(contentWindow.postMessage.mock.calls.length).toBeGreaterThan(0);

    for (const call of contentWindow.postMessage.mock.calls) {
      expect(call[1]).toBe(origin);
      expect(call[1]).not.toBe('*');
    }
  });

  it('takes the corner the widget asks for, and only one of two words', () => {
    run();

    fromWidget({ type: 'omniai:v1:ready', position: 'Left' });

    expect(frame()!.style.left).toBe('0px');
    expect(frame()!.style.right).toBe('auto');

    // Anything that is not "Left" leaves the default corner in place.
    retireFrames();
    delete (window as unknown as Record<string, unknown>)['__omniaiWidgetLoaded'];
    run();

    fromWidget({ type: 'omniai:v1:ready', position: 'anything else' });

    expect(frame()!.style.right).toBe('0px');
  });

  it('makes room when the widget opens, and takes it back when it closes', () => {
    run();

    fromWidget({ type: 'omniai:v1:ready', position: 'Right' });

    const closedWidth = frame()!.style.width;

    fromWidget({ type: 'omniai:v1:open' });

    const openWidth = frame()!.style.width;

    expect(Number.parseInt(openWidth, 10)).toBeGreaterThan(Number.parseInt(closedWidth, 10));

    fromWidget({ type: 'omniai:v1:close' });

    expect(frame()!.style.width).toBe(closedWidth);
  });

  it('ignores a message from the wrong origin or the wrong frame', () => {
    run();

    fromWidget({ type: 'omniai:v1:ready', position: 'Right' });

    const closedWidth = frame()!.style.width;

    // The origin check alone would accept any other OmniAI frame on the page; the
    // source check alone would accept this frame after it had been navigated away.
    fromWidget({ type: 'omniai:v1:open' }, 'https://attacker.example');
    fromWidget({ type: 'omniai:v1:open' }, origin, { name: 'some other frame' });

    expect(frame()!.style.width).toBe(closedWidth);
  });

  it('ignores anything that is not a message in its own protocol', () => {
    run();

    fromWidget({ type: 'omniai:v1:ready', position: 'Right' });

    const closedWidth = frame()!.style.width;

    for (const data of [null, 'omniai:v1:open', 7, { type: 7 }, { type: 'other:open' }]) {
      fromWidget(data);
    }

    // A v2 message is ignored too: a v1 loader must not guess at v2 semantics.
    fromWidget({ type: 'omniai:v2:takeover' });

    expect(frame()!.style.width).toBe(closedWidth);
  });

  it('builds no HTML from a string and evaluates nothing', () => {
    // A script running on somebody else's website is the last place for any of
    // these. The comment at the top of the loader says so; this checks it.
    const code = SOURCE.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');

    for (const forbidden of ['eval(', 'document.write', 'innerHTML', 'outerHTML', 'Function(']) {
      expect(code).not.toContain(forbidden);
    }
  });
});
