import { TestBed } from '@angular/core/testing';
import { SoundPlatform, SOUND_PLATFORM } from './sound-platform';
import { CLAIM_HOLD_MS, ELECTION_MS, HIDDEN_TAB_DELAY_MS, TabClaim } from './tab-claim';

/** A lock manager with one lock, as one browser has. */
class FakeLocks {
  held = false;
  releases = 0;

  request(
    _name: string,
    _options: unknown,
    callback: (lock: unknown) => Promise<void> | undefined,
  ): Promise<void> {
    if (this.held) {
      callback(null);

      return Promise.resolve();
    }

    this.held = true;

    return Promise.resolve(callback({ name: 'lock' })).then(() => {
      this.held = false;
      this.releases++;
    });
  }
}

class FakeChannel {
  posted: unknown[] = [];
  onmessage: ((event: MessageEvent) => void) | null = null;

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  hear(data: unknown): void {
    this.onmessage?.({ data } as MessageEvent);
  }
}

describe('TabClaim', () => {
  let channel: FakeChannel;
  let locks: FakeLocks;
  let visible: boolean;

  function create(overrides: Partial<SoundPlatform> = {}): TabClaim {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SOUND_PLATFORM,
          useValue: {
            locks,
            createChannel: () => channel,
            isVisible: () => visible,
            ...overrides,
          },
        },
      ],
    });

    return TestBed.inject(TabClaim);
  }

  /** Runs a claim through its timers. */
  async function settle(claim: Promise<boolean>, ms: number): Promise<boolean> {
    await vi.advanceTimersByTimeAsync(ms);

    return claim;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    channel = new FakeChannel();
    locks = new FakeLocks();
    visible = true;
  });

  afterEach(() => vi.useRealTimers());

  it('lets a visible tab play at once, and says so on the channel', async () => {
    expect(await create().claim()).toBe(true);
    expect(channel.posted).toContainEqual({ kind: 'visible' });
  });

  it('holds the lock for two seconds, so a second tab stands aside', async () => {
    const claim = create();

    expect(await claim.claim()).toBe(true);
    expect(await claim.claim()).toBe(false);

    await vi.advanceTimersByTimeAsync(CLAIM_HOLD_MS);

    expect(locks.releases).toBe(1);
    expect(await claim.claim()).toBe(true);
  });

  it('lets a hidden tab play when no visible tab has spoken', async () => {
    visible = false;

    const claim = create().claim();

    expect(await settle(claim, HIDDEN_TAB_DELAY_MS)).toBe(true);
    expect(channel.posted).toEqual([]);
  });

  it('makes a hidden tab stand aside for a visible one', async () => {
    visible = false;

    const claim = create().claim();

    channel.hear({ kind: 'visible' });

    expect(await settle(claim, HIDDEN_TAB_DELAY_MS)).toBe(false);
  });

  it('elects over the channel without Web Locks: the lowest id plays', async () => {
    const tab = create({ locks: null });
    const claim = tab.claim();

    channel.hear({ kind: 'claim', id: '0' });

    expect(await settle(claim, ELECTION_MS)).toBe(false);
  });

  it('wins that election against a higher id', async () => {
    const tab = create({ locks: null });

    // A rival is heard only after this tab's own claim, inside the election.
    const claim = tab.claim();

    await Promise.resolve();
    channel.hear({ kind: 'claim', id: '~' });
    await vi.advanceTimersByTimeAsync(ELECTION_MS);

    expect(await claim).toBe(true);
  });

  it('stands aside for two seconds after hearing another tab win', async () => {
    const tab = create({ locks: null });

    channel.hear({ kind: 'claim', id: '~' });

    expect(await tab.claim()).toBe(false);

    await vi.advanceTimersByTimeAsync(CLAIM_HOLD_MS);

    const next = tab.claim();

    await vi.advanceTimersByTimeAsync(ELECTION_MS);

    expect(await next).toBe(true);
  });

  it('plays alone with neither Web Locks nor a channel', async () => {
    expect(await create({ locks: null, createChannel: () => null }).claim()).toBe(true);
  });

  it('plays alone when the channel cannot be opened', async () => {
    const claim = create({
      locks: null,
      createChannel: () => {
        throw new Error('blocked');
      },
    }).claim();

    expect(await claim).toBe(true);
  });

  it('plays alone when the lock manager refuses', async () => {
    const refusing = { request: () => Promise.reject(new Error('opaque origin')) };

    expect(await create({ locks: refusing as unknown as LockManager }).claim()).toBe(true);
  });
});
