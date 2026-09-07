import { HostedChatSessionStore } from './hosted-chat-session';

describe('HostedChatSessionStore', () => {
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';
  const conversationId = '11111111-1111-7111-8111-111111111111';

  let store: HostedChatSessionStore;

  beforeEach(() => {
    store = new HostedChatSessionStore();
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  afterEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  it('remembers a conversation and reads it back', () => {
    store.write(publicKey, conversationId);

    expect(store.read(publicKey)).toBe(conversationId);
  });

  it('remembers nothing for an endpoint it has not seen', () => {
    expect(store.read(publicKey)).toBeNull();
  });

  it('writes to sessionStorage and never to localStorage', () => {
    // The credential cookie is session-scoped, so remembering the conversation
    // for longer would leave a resume id whose cookie is already gone -- and would
    // offer a stranger's conversation on a shared computer the next morning.
    store.write(publicKey, conversationId);

    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(1);
  });

  it('stores the conversation id and nothing that could be a secret', () => {
    store.write(publicKey, conversationId);

    const stored = JSON.stringify(window.sessionStorage);

    expect(stored).toContain(conversationId);

    // No credential, no digest, no tenant, no agent configuration.
    for (const leak of ['token', 'secret', 'hash', 'tenant', 'instruction', 'agent']) {
      expect(stored.toLowerCase()).not.toContain(leak);
    }
  });

  it('scopes what it remembers by public key', () => {
    // Two chats hosted on one origin -- a staging endpoint and a live one, or two
    // different businesses -- must never resume into each other's conversation.
    store.write(publicKey, conversationId);
    store.write('bbbbbbbbbbbbbbbbbbbbbb', '22222222-2222-7222-8222-222222222222');

    expect(store.read(publicKey)).toBe(conversationId);
    expect(store.read('bbbbbbbbbbbbbbbbbbbbbb')).toBe('22222222-2222-7222-8222-222222222222');
  });

  it('forgets one endpoint without disturbing another', () => {
    store.write(publicKey, conversationId);
    store.write('bbbbbbbbbbbbbbbbbbbbbb', '22222222-2222-7222-8222-222222222222');

    store.clear(publicKey);

    expect(store.read(publicKey)).toBeNull();
    expect(store.read('bbbbbbbbbbbbbbbbbbbbbb')).toBe('22222222-2222-7222-8222-222222222222');
  });

  it('treats a blank stored value as nothing remembered', () => {
    // A corrupted entry must cost no request and produce no confusing not-found.
    window.sessionStorage.setItem(`omniai.webchat.conversation.${publicKey}`, '   ');

    expect(store.read(publicKey)).toBeNull();
  });

  it('survives storage that throws on read', () => {
    // Some contexts throw outright rather than returning null -- a browser set to
    // block site data, a sandboxed frame. A chat page that crashed because it could
    // not read a convenience value would be a worse failure than one that starts a
    // new conversation.
    const original = window.sessionStorage.getItem;

    window.sessionStorage.getItem = () => {
      throw new Error('Access denied.');
    };

    try {
      expect(store.read(publicKey)).toBeNull();
    } finally {
      window.sessionStorage.getItem = original;
    }
  });

  it('survives storage that throws on write', () => {
    const original = window.sessionStorage.setItem;

    window.sessionStorage.setItem = () => {
      throw new Error('Quota exceeded.');
    };

    try {
      expect(() => store.write(publicKey, conversationId)).not.toThrow();
    } finally {
      window.sessionStorage.setItem = original;
    }
  });

  it('survives storage that throws on clear', () => {
    const original = window.sessionStorage.removeItem;

    window.sessionStorage.removeItem = () => {
      throw new Error('Access denied.');
    };

    try {
      expect(() => store.clear(publicKey)).not.toThrow();
    } finally {
      window.sessionStorage.removeItem = original;
    }
  });

  it('clearing something it never stored is not an error', () => {
    expect(() => store.clear(publicKey)).not.toThrow();
    expect(store.read(publicKey)).toBeNull();
  });
});
