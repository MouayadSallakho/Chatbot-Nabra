import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/core';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { DesktopAlerts } from './desktop-alerts';
import { RESUME_WAIT_MS, SoundPlayer } from './sound-player';
import { QUIET_WINDOW_MS } from './sound-rules';
import { SOUND_PLATFORM, SoundPlatform } from './sound-platform';
import { SoundSettings } from './sound-settings';
import { TabClaim } from './tab-claim';

/** Web Audio, in the handful of calls the player makes. */
class FakeContext {
  state: AudioContextState = 'running';
  resumes = 0;
  destination = {};
  decoded: ArrayBuffer[] = [];
  started: { gain: number; buffer: unknown }[] = [];
  failDecode = false;

  async resume(): Promise<void> {
    this.resumes++;
    this.state = 'running';
  }

  async decodeAudioData(data: ArrayBuffer): Promise<unknown> {
    if (this.failDecode) {
      throw new Error('bad file');
    }

    this.decoded.push(data);

    return { from: data };
  }

  createGain(): { gain: { value: number }; connect: () => void } {
    return { gain: { value: 1 }, connect: () => undefined };
  }

  createBufferSource(): unknown {
    const started = this.started;
    let gainNode: { gain: { value: number } } | null = null;

    const source = {
      buffer: null as unknown,
      connect(node: { gain: { value: number } }): void {
        gainNode = node;
      },
      start(): void {
        started.push({ gain: gainNode!.gain.value, buffer: source.buffer });
      },
    };

    return source;
  }
}

describe('SoundPlayer', () => {
  let context: FakeContext | null;
  let contextsMade: number;
  let fetched: string[];
  let active: boolean;
  let claimAnswer: boolean;
  let fetchImpl: (url: string) => Promise<Response>;

  const ok = async (url: string): Promise<Response> => {
    fetched.push(url);

    return { ok: true, arrayBuffer: async () => new ArrayBuffer(8) } as Response;
  };

  function create(stored: Record<string, string> = {}): SoundPlayer {
    const platform: Partial<SoundPlatform> = {
      createAudioContext: () => {
        contextsMade++;

        return context as unknown as AudioContext;
      },
      fetch: (url) => fetchImpl(url),
      isUserActive: () => active,
      locks: null,
      createChannel: () => null,
      notifications: null,
      isVisible: () => true,
      hasFocus: () => true,
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage(stored) },
        { provide: SOUND_PLATFORM, useValue: platform },
        { provide: TabClaim, useValue: { claim: async () => claimAnswer } },
      ],
    });

    return TestBed.inject(SoundPlayer);
  }

  /** Lets timers and the promises behind them run. */
  const run = async (ms = 0): Promise<void> => {
    await vi.advanceTimersByTimeAsync(ms);
  };

  const press = (): void => {
    TestBed.inject(DOCUMENT).dispatchEvent(new Event('pointerdown'));
  };

  beforeEach(() => {
    vi.useFakeTimers();
    context = new FakeContext();
    contextsMade = 0;
    fetched = [];
    active = true;
    claimAnswer = true;
    fetchImpl = ok;
  });

  afterEach(() => {
    // Lets a player armed by this test take its listeners off the shared document.
    active = true;
    press();
    vi.useRealTimers();
  });

  describe('the unlock', () => {
    it('creates no audio context before the first gesture', () => {
      create().arm();

      expect(contextsMade).toBe(0);
    });

    it('creates it on the first gesture, and only once', () => {
      create().arm();

      press();
      press();

      expect(contextsMade).toBe(1);
    });

    it('waits for a real gesture when the browser says this was not one', () => {
      create().arm();

      active = false;
      press();
      expect(contextsMade).toBe(0);

      active = true;
      press();
      expect(contextsMade).toBe(1);
    });

    it('never throws where audio cannot be created', () => {
      const player = create();

      TestBed.inject(SOUND_PLATFORM).createAudioContext = () => {
        throw new Error('no audio');
      };

      expect(() => player.unlock()).not.toThrow();
    });

    it('wakes a suspended context', () => {
      const player = create();

      context!.state = 'suspended';
      player.unlock();

      expect(context!.resumes).toBe(1);
    });
  });

  describe('playing', () => {
    it('skips silently before the unlock, and fetches nothing', async () => {
      const player = create();

      player.request({ sound: 'ai-failed' });
      await run();

      expect(fetched).toEqual([]);
      expect(context!.started).toEqual([]);
    });

    it('fetches and decodes nothing until a sound is needed', async () => {
      create().unlock();
      await run(QUIET_WINDOW_MS);

      expect(fetched).toEqual([]);
    });

    it('plays by name at the chosen volume', async () => {
      const player = create({ 'omniai.sounds-volume': '40' });

      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run();

      expect(fetched).toEqual(['sounds/kalimba-ai-failed.v1.wav']);
      expect(context!.started).toHaveLength(1);
      expect(context!.started[0].gain).toBeCloseTo(0.4);
    });

    it('fetches and decodes each file once', async () => {
      const player = create();

      player.unlock();
      player.request({ sound: 'needs-person' });
      await run();
      await run(QUIET_WINDOW_MS);
      player.request({ sound: 'needs-person' });
      await run();

      expect(fetched).toHaveLength(1);
      expect(context!.decoded).toHaveLength(1);
      expect(context!.started).toHaveLength(2);
    });

    it.each([
      ['needs-person', 'sounds/kalimba-needs-person.v1.wav'],
      ['new-message', 'sounds/kalimba-new-message.v1.wav'],
      ['ai-failed', 'sounds/kalimba-ai-failed.v1.wav'],
    ] as const)('maps %s to its versioned file', async (sound, file) => {
      const player = create();

      player.unlock();
      player.request({ sound });
      await run();

      expect(fetched).toEqual([file]);
    });

    it('stays silent with the setting off', async () => {
      const player = create({ 'omniai.sounds': 'off' });

      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run();

      expect(fetched).toEqual([]);
      expect(context!.started).toEqual([]);
    });

    it('honours a setting turned off while the sound waited', async () => {
      const player = create();

      player.unlock();
      player.request({ sound: 'new-message' });
      await run();
      player.request({ sound: 'ai-failed' });
      TestBed.inject(SoundSettings).setEnabled(false);
      await run(QUIET_WINDOW_MS);

      expect(context!.started).toHaveLength(1);
    });

    it('lets one tab play: a lost claim is silent', async () => {
      claimAnswer = false;

      const player = create();

      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run();

      expect(fetched).toEqual([]);
      expect(context!.started).toEqual([]);
    });

    it('is silent, and retries nothing, when a file will not decode', async () => {
      const player = create();

      context!.failDecode = true;
      player.unlock();

      await expect(
        (async () => {
          player.request({ sound: 'ai-failed' });
          await run();
          await run(QUIET_WINDOW_MS);
          player.request({ sound: 'ai-failed' });
          await run();
        })(),
      ).resolves.toBeUndefined();

      expect(fetched).toHaveLength(1);
      expect(context!.started).toEqual([]);
    });

    it('is silent for a missing file', async () => {
      fetchImpl = async (url) => {
        fetched.push(url);

        return { ok: false } as Response;
      };

      const player = create();

      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run();

      expect(context!.started).toEqual([]);
    });

    it('tries the network again next time after an offline moment', async () => {
      const player = create();

      fetchImpl = async () => {
        throw new TypeError('offline');
      };
      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run();
      await run(QUIET_WINDOW_MS);

      fetchImpl = ok;
      player.request({ sound: 'ai-failed' });
      await run();

      expect(context!.started).toHaveLength(1);
    });

    it('does not hold a sound for a context that will not wake', async () => {
      const player = create();

      context!.state = 'suspended';
      context!.resume = () => new Promise<void>(() => undefined);
      player.unlock();
      player.request({ sound: 'ai-failed' });
      await run(RESUME_WAIT_MS);

      expect(context!.started).toEqual([]);
    });
  });

  describe('the test button', () => {
    it("plays the owner's sound and says it was heard", async () => {
      const player = create();
      const heard = player.test();

      await run();

      expect(await heard).toBe(true);
      expect(fetched).toEqual(['sounds/kalimba-needs-person.v1.wav']);
      expect(context!.started).toHaveLength(1);
    });

    it('plays with sounds switched off, because it was asked for by name', async () => {
      const heard = create({ 'omniai.sounds': 'off' }).test();

      await run();

      expect(await heard).toBe(true);
    });

    it('does not need to win the tab election', async () => {
      claimAnswer = false;

      const heard = create().test();

      await run();

      expect(await heard).toBe(true);
    });

    it('answers false where there is no audio at all', async () => {
      context = null;

      const heard = create().test();

      await run();

      expect(await heard).toBe(false);
    });

    it('answers false for a second press inside the window', async () => {
      const player = create();

      player.test();
      await run();

      expect(await player.test()).toBe(false);
      expect(context!.started).toHaveLength(1);
    });
  });

  describe('the desktop alert', () => {
    const alert = { title: 'Visitor 7KQ2MX', body: 'Needs a person', conversationId: 'c1' };

    it('goes with the sound of the tab that won', async () => {
      const player = create();
      const show = vi
        .spyOn(TestBed.inject(DesktopAlerts), 'show')
        .mockImplementation(() => undefined);

      player.unlock();
      player.request({ sound: 'needs-person', alert });
      await run();

      expect(show).toHaveBeenCalledWith(alert);
    });

    it('is left to the tab that won', async () => {
      claimAnswer = false;

      const player = create();
      const show = vi
        .spyOn(TestBed.inject(DesktopAlerts), 'show')
        .mockImplementation(() => undefined);

      player.request({ sound: 'needs-person', alert });
      await run();

      expect(show).not.toHaveBeenCalled();
    });

    it('still shows with sounds off and alerts on', async () => {
      const player = create({ 'omniai.sounds': 'off', 'omniai.desktop-alerts': 'on' });
      const show = vi
        .spyOn(TestBed.inject(DesktopAlerts), 'show')
        .mockImplementation(() => undefined);

      player.request({ sound: 'needs-person', alert });
      await run();

      expect(show).toHaveBeenCalledWith(alert);
      expect(fetched).toEqual([]);
    });
  });
});
