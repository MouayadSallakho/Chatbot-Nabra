import { EnvironmentInjector, Injectable, effect, inject } from '@angular/core';
import { PermissionService } from '@app/core/authorization/permission-service';
import type { SoundPlayer } from './sound-player';
import { SoundRequest } from './sound-names';
import { SoundSettings } from './sound-settings';

/**
 * The always-loaded door to the sounds.
 *
 * <strong>Everything that makes a noise is a lazy chunk.</strong> This class is
 * what the shell and the account menu hold; the player, its rules and the audio
 * files arrive later, and the files only when a sound is first needed. Failing to
 * load them is silence: a sound is never the only signal, and never an error.
 */
@Injectable({ providedIn: 'root' })
export class Sounds {
  private readonly injector = inject(EnvironmentInjector);
  private readonly permissions = inject(PermissionService);
  private readonly settings = inject(SoundSettings);

  private started = false;
  private player: Promise<SoundPlayer | null> | null = null;

  /** Prepares the sounds for members who can hear them. Called by the shell after first render. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;

    effect(
      () => {
        if (
          this.permissions.has('conversations.view') &&
          (this.settings.enabled() || this.settings.desktopAlerts())
        ) {
          void this.load().then((player) => player?.arm());
          void this.startSignals();
        }
      },
      { injector: this.injector },
    );
  }

  /** Asks for a sound. What decides whether and when it plays is in `SoundPlayer`. */
  request(request: SoundRequest): void {
    void this.load().then((player) => player?.request(request));
  }

  /** The person's own gesture, passed on: from here the browser allows audio. */
  unlock(): void {
    void this.load().then((player) => player?.unlock());
  }

  /** Plays the test sound. True when it was heard. */
  async test(): Promise<boolean> {
    return (await this.load())?.test() ?? false;
  }

  /** What decides when a sound is due. A lazy chunk too, and its absence is silence. */
  private async startSignals(): Promise<void> {
    try {
      const { InboxSignals } = await import('./inbox-signals');

      this.injector.get(InboxSignals).start();
    } catch {
      // A chunk that did not arrive: no sounds, and the screen still says it all.
    }
  }

  private load(): Promise<SoundPlayer | null> {
    this.player ??= import('./sound-player')
      .then(({ SoundPlayer }) => this.injector.get(SoundPlayer))
      .catch(() => {
        // A chunk that did not arrive may arrive next time.
        this.player = null;

        return null;
      });

    return this.player;
  }
}
