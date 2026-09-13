import { EmbedBridge } from './embed-bridge';

describe('EmbedBridge', () => {
  const parentOrigin = 'https://shop.example.com';

  let bridge: EmbedBridge;
  let parent: { postMessage: ReturnType<typeof vi.fn> };
  let realParent: Window;

  /**
   * Delivers a message as if it came from the framing page.
   *
   * `source` is defined after construction because the constructor only accepts a
   * real window, and the point of these tests is what the bridge does with a
   * source that is or is not the parent.
   */
  function deliver(data: unknown, origin: string, source: unknown = parent): void {
    const event = new MessageEvent('message', { data, origin });

    Object.defineProperty(event, 'source', { value: source });

    window.dispatchEvent(event);
  }

  beforeEach(() => {
    parent = { postMessage: vi.fn() };
    realParent = window.parent;

    Object.defineProperty(window, 'parent', { value: parent, configurable: true });

    bridge = new EmbedBridge();
  });

  afterEach(() => {
    bridge.ngOnDestroy();
    Object.defineProperty(window, 'parent', { value: realParent, configurable: true });
  });

  it('learns the parent origin from the browser, not from anything the page says', async () => {
    const connected = bridge.connect();

    // The page claims to be somewhere else in the payload. Only event.origin,
    // which the browser fills in, is read.
    deliver({ type: 'omniai:v1:init', origin: 'https://attacker.example' }, parentOrigin);

    await expect(connected).resolves.toBe(parentOrigin);
  });

  it('ignores a handshake from a frame that is not the parent', async () => {
    const connected = bridge.connect();

    deliver({ type: 'omniai:v1:init' }, 'https://other.example', { postMessage: vi.fn() });
    deliver({ type: 'omniai:v1:init' }, parentOrigin);

    await expect(connected).resolves.toBe(parentOrigin);
  });

  it('refuses an opaque origin, because no allowlist entry could ever match it', async () => {
    vi.useFakeTimers();

    try {
      const connected = bridge.connect();

      // A sandboxed or data: document reports "null". Naming it to the server as
      // a website would be a lie.
      deliver({ type: 'omniai:v1:init' }, 'null');

      await vi.advanceTimersByTimeAsync(5_000);

      await expect(connected).resolves.toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('refuses an origin that is not http or https', async () => {
    vi.useFakeTimers();

    try {
      const connected = bridge.connect();

      deliver({ type: 'omniai:v1:init' }, 'file://');

      await vi.advanceTimersByTimeAsync(5_000);

      await expect(connected).resolves.toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('ignores anything outside its own protocol', async () => {
    vi.useFakeTimers();

    try {
      const connected = bridge.connect();

      for (const data of [null, 'omniai:v1:init', 42, { type: 42 }, { type: 'other:init' }]) {
        deliver(data, parentOrigin);
      }

      await vi.advanceTimersByTimeAsync(5_000);

      await expect(connected).resolves.toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  it('never posts before it knows where to post', () => {
    bridge.ready('Right');
    bridge.open();
    bridge.close();

    // The alternative to staying silent would be postMessage(..., '*'), which is
    // the one thing this channel must never do.
    expect(parent.postMessage).not.toHaveBeenCalled();
  });

  it('names the exact parent origin on every message it sends', async () => {
    const connected = bridge.connect();

    deliver({ type: 'omniai:v1:init' }, parentOrigin);
    await connected;

    bridge.ready('Left');
    bridge.open();
    bridge.close();

    expect(parent.postMessage).toHaveBeenCalledTimes(3);

    for (const call of parent.postMessage.mock.calls) {
      expect(call[1]).toBe(parentOrigin);
      expect(call[1]).not.toBe('*');
    }

    expect(parent.postMessage.mock.calls[0][0]).toEqual({
      type: 'omniai:v1:ready',
      position: 'Left',
    });
  });

  it('sends nothing but structure: no credential, no text, no workspace', async () => {
    const connected = bridge.connect();

    deliver({ type: 'omniai:v1:init' }, parentOrigin);
    await connected;

    bridge.ready('Right');
    bridge.open();

    const sent = JSON.stringify(parent.postMessage.mock.calls.map((call) => call[0]));

    for (const leak of ['credential', 'token', 'tenant', 'agentId', 'message', 'conversation']) {
      expect(sent.toLowerCase()).not.toContain(leak.toLowerCase());
    }
  });

  it('takes viewport changes from the established parent only', async () => {
    const connected = bridge.connect();

    deliver({ type: 'omniai:v1:init' }, parentOrigin);
    await connected;

    expect(bridge.compact()).toBe(false);

    deliver({ type: 'omniai:v1:viewport', compact: true }, parentOrigin);

    expect(bridge.compact()).toBe(true);

    // A different origin, and a different frame, are both ignored.
    deliver({ type: 'omniai:v1:viewport', compact: false }, 'https://other.example');
    deliver({ type: 'omniai:v1:viewport', compact: false }, parentOrigin, { postMessage: vi.fn() });

    expect(bridge.compact()).toBe(true);
  });

  it('reports no parent at all when it is not framed', async () => {
    Object.defineProperty(window, 'parent', { value: window, configurable: true });

    const standalone = new EmbedBridge();

    try {
      await expect(standalone.connect()).resolves.toBeNull();
    } finally {
      standalone.ngOnDestroy();
    }
  });

  it('stops listening when it is destroyed', async () => {
    const connected = bridge.connect();

    deliver({ type: 'omniai:v1:init' }, parentOrigin);
    await connected;

    bridge.ngOnDestroy();

    deliver({ type: 'omniai:v1:viewport', compact: true }, parentOrigin);

    expect(bridge.compact()).toBe(false);
  });
});
