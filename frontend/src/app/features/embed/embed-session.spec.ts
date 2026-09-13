import { EmbedSessionStore } from './embed-session';

describe('EmbedSessionStore', () => {
  const publicKey = 'aaaaaaaaaaaaaaaaaaaaaa';
  const session = {
    conversationId: '11111111-1111-7111-8111-111111111111',
    credential: 'ZmFrZS1jcmVkZW50aWFsLWZvci10ZXN0cw',
  };

  let store: EmbedSessionStore;

  beforeEach(() => {
    store = new EmbedSessionStore();
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  afterEach(() => {
    window.sessionStorage.clear();
    window.localStorage.clear();
  });

  it('remembers a conversation and reads it back', () => {
    store.write(publicKey, session);

    expect(store.read(publicKey)).toEqual(session);
  });

  it('remembers nothing for a widget it has not seen', () => {
    expect(store.read(publicKey)).toBeNull();
  });

  it('writes to sessionStorage and never to localStorage', () => {
    // A credential that outlived the tab would be a working secret still sitting
    // on a shared computer the next morning.
    store.write(publicKey, session);

    expect(window.localStorage.length).toBe(0);
    expect(window.sessionStorage.length).toBe(1);
  });

  it('scopes what it remembers by public key', () => {
    const other = {
      conversationId: '22222222-2222-7222-8222-222222222222',
      credential: 'b3RoZXItY3JlZGVudGlhbA',
    };

    store.write(publicKey, session);
    store.write('bbbbbbbbbbbbbbbbbbbbbb', other);

    expect(store.read(publicKey)).toEqual(session);
    expect(store.read('bbbbbbbbbbbbbbbbbbbbbb')).toEqual(other);
  });

  it('forgets the credential as well as the conversation', () => {
    store.write(publicKey, session);
    store.clear(publicKey);

    expect(store.read(publicKey)).toBeNull();
    expect(JSON.stringify(window.sessionStorage)).not.toContain(session.credential);
  });

  it('treats a malformed entry as absent', () => {
    for (const junk of ['', '   ', 'not json', '{}', '{"conversationId":"only"}', 'null', '[]']) {
      window.sessionStorage.setItem(`omniai.embed.session.${publicKey}`, junk);

      expect(store.read(publicKey)).toBeNull();
    }
  });

  it('keeps working in memory when storage refuses', () => {
    // A browser configured to block site data throws on every access. The widget
    // must still work; only resuming after a reload is lost.
    const storage = window.sessionStorage;
    const throwing = {
      getItem: () => {
        throw new Error('blocked');
      },
      setItem: () => {
        throw new Error('blocked');
      },
      removeItem: () => {
        throw new Error('blocked');
      },
    };

    Object.defineProperty(window, 'sessionStorage', { value: throwing, configurable: true });

    try {
      expect(() => store.write(publicKey, session)).not.toThrow();
      expect(store.read(publicKey)).toEqual(session);
      expect(() => store.clear(publicKey)).not.toThrow();
      expect(store.read(publicKey)).toBeNull();
    } finally {
      Object.defineProperty(window, 'sessionStorage', { value: storage, configurable: true });
    }
  });
});
