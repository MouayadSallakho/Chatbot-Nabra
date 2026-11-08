import { DOCUMENT, Injectable, inject } from '@angular/core';
import { DesktopAlerts } from './desktop-alerts';
import { SoundName, SoundRequest } from './sound-names';
import { SOUND_PLATFORM } from './sound-platform';
import { SoundCandidate, SoundScheduler } from './sound-rules';
import { SoundSettings } from './sound-settings';
import { TabClaim } from './tab-claim';

/** Versioned, like the icons: files in `public/` are not content-hashed. */
const SOUND_FILES: Readonly<Record<SoundName, string>> = {
  'needs-person': 'sounds/kalimba-needs-person.v1.wav',
  'new-message': 'sounds/kalimba-new-message.v1.wav',
  'ai-failed': 'sounds/kalimba-ai-failed.v1.wav',
};

const TEST_SOUND: SoundName = 'needs-person';

/** How long a suspended audio context gets to wake before the sound is skipped. */
export const RESUME_WAIT_MS = 300;

/** The events a browser may count as a person's gesture; touch counts on the way up. */
const GESTURE_EVENTS = ['pointerdown', 'pointerup', 'keydown'] as const;

/**
 * Plays the three sounds.
 *
 * <strong>Silent, never broken.</strong> Audio that is missing, locked or undecodable
 * is skipped without an error: the same facts are on the screen, and there is nothing
 * a person could do about it.
 *
 * <strong>Each file is fetched and decoded once,</strong> the first time it is needed
 * after the browser has allowed audio.
 *
 * <strong>Nothing plays before the person's first click or key press on the page,</strong>
 * which is the browser's rule, and creating the audio context earlier would only
 * fill the console with warnings.
 */
@Injectable({ providedIn: 'root' })
export class SoundPlayer {
  private readonly platform = inject(SOUND_PLATFORM);
  private readonly settings = inject(SoundSettings);
  private readonly alerts = inject(DesktopAlerts);
  private readonly tab = inject(TabClaim);
  private readonly document = inject(DOCUMENT);

  private readonly scheduler = new SoundScheduler((candidate) => void this.fire(candidate));
  private readonly buffers = new Map<SoundName, Promise<AudioBuffer | null>>();

  private context: AudioContext | null = null;
  private armed = false;

  /** Waits, once, for the person's first gesture. */
  arm(): void {
    if (this.armed) {
      return;
    }

    this.armed = true;

    const options = { capture: true, passive: true };
    const onGesture = (): void => {
      // A key like Shift is an event but not a gesture: wait for one that is.
      if (!this.platform.isUserActive()) {
        return;
      }

      for (const name of GESTURE_EVENTS) {
        this.document.removeEventListener(name, onGesture, options);
      }

      this.unlock();
    };

    for (const name of GESTURE_EVENTS) {
      this.document.addEventListener(name, onGesture, options);
    }
  }

  /** Creates or wakes the audio context. Call it from a person's gesture. */
  unlock(): void {
    try {
      this.context ??= this.platform.createAudioContext();

      if (this.context?.state === 'suspended') {
        void this.context.resume().catch(() => undefined);
      }
    } catch {
      this.context = null;
    }
  }

  /** Asks for a sound and, if it carries one, its desktop alert. */
  request(request: SoundRequest): void {
    if (this.settings.enabled() || (request.alert && this.settings.desktopAlerts())) {
      this.scheduler.request(request);
    }
  }

  /**
   * Plays the test sound, even with sounds switched off: it was asked for by name.
   * True when it was heard.
   */
  test(): Promise<boolean> {
    this.unlock();

    return new Promise((resolve) => {
      if (!this.scheduler.request({ sound: TEST_SOUND, test: true, settled: resolve })) {
        resolve(false);
      }
    });
  }

  private async fire(candidate: SoundCandidate): Promise<void> {
    let heard = false;

    try {
      if (candidate.test) {
        heard = await this.play(candidate.sound);
      } else if (await this.tab.claim()) {
        // Read now, not when it was asked for: it may have been turned off since.
        if (this.settings.enabled()) {
          heard = await this.play(candidate.sound);
        }

        if (candidate.alert) {
          this.alerts.show(candidate.alert);
        }
      }
    } catch {
      heard = false;
    }

    candidate.settled?.(heard);
  }

  private async play(name: SoundName): Promise<boolean> {
    const context = this.context;

    if (context === null) {
      return false;
    }

    const buffer = await this.load(context, name);

    if (buffer === null || !(await this.awake(context))) {
      return false;
    }

    const source = context.createBufferSource();
    const gain = context.createGain();

    gain.gain.value = this.settings.volume() / 100;
    source.buffer = buffer;
    source.connect(gain);
    gain.connect(context.destination);
    source.start();

    return true;
  }

  /** A suspended context would otherwise hold the sound and play it long after the fact. */
  private async awake(context: AudioContext): Promise<boolean> {
    if (context.state !== 'running') {
      await Promise.race([
        context.resume().catch(() => undefined),
        new Promise((resolve) => setTimeout(resolve, RESUME_WAIT_MS)),
      ]);
    }

    return context.state === 'running';
  }

  private load(context: AudioContext, name: SoundName): Promise<AudioBuffer | null> {
    let buffer = this.buffers.get(name);

    if (buffer === undefined) {
      buffer = this.fetchAndDecode(context, name);
      this.buffers.set(name, buffer);
    }

    return buffer;
  }

  private async fetchAndDecode(
    context: AudioContext,
    name: SoundName,
  ): Promise<AudioBuffer | null> {
    let response: Response;

    try {
      response = await this.platform.fetch(SOUND_FILES[name]);
    } catch {
      // The network, not the file: the next time it is needed may go better.
      this.buffers.delete(name);

      return null;
    }

    try {
      return response.ok ? await context.decodeAudioData(await response.arrayBuffer()) : null;
    } catch {
      return null;
    }
  }
}
