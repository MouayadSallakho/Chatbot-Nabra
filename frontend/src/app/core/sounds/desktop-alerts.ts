import { DOCUMENT, Injectable, NgZone, inject } from '@angular/core';
import { Router } from '@angular/router';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { DesktopAlert } from './sound-names';
import { SOUND_PLATFORM } from './sound-platform';
import { SoundSettings } from './sound-settings';

export type DesktopAlertsAnswer = 'on' | 'refused' | 'unsupported';

/**
 * Desktop alerts: off until a person turns them on, and quiet while they are looking.
 *
 * Nothing here reads a transcript. An alert says who (the customer's label) and why,
 * and clicking it opens the conversation through the ordinary authorised page.
 */
@Injectable({ providedIn: 'root' })
export class DesktopAlerts {
  private readonly platform = inject(SOUND_PLATFORM);
  private readonly settings = inject(SoundSettings);
  private readonly language = inject(APP_LANGUAGE);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly view = inject(DOCUMENT).defaultView;

  readonly supported = this.platform.notifications !== null;

  /** Whether this browser has already said no to this site. */
  blocked(): boolean {
    return this.platform.notifications?.permission() === 'denied';
  }

  /**
   * Turns alerts on, asking the browser when it has not been asked yet.
   *
   * <strong>Call it directly from the person's click.</strong> The browser's prompt
   * has to appear right after their own gesture, so nothing is awaited before it.
   */
  async enable(): Promise<DesktopAlertsAnswer> {
    const notifications = this.platform.notifications;

    if (notifications === null) {
      return 'unsupported';
    }

    const answer =
      notifications.permission() === 'granted' ? 'granted' : await notifications.request();

    if (answer !== 'granted') {
      return 'refused';
    }

    this.settings.setDesktopAlerts(true);

    return 'on';
  }

  disable(): void {
    this.settings.setDesktopAlerts(false);
  }

  /** Shows an alert, if they are on, allowed and the person is looking elsewhere. */
  show(alert: DesktopAlert): void {
    const notifications = this.platform.notifications;

    if (
      notifications === null ||
      !this.settings.desktopAlerts() ||
      notifications.permission() !== 'granted' ||
      (this.platform.isVisible() && this.platform.hasFocus())
    ) {
      return;
    }

    try {
      const notification = notifications.show(alert.title, {
        body: alert.body,
        tag: alert.conversationId,
        lang: this.language,
        dir: directionOf(this.language),
      });

      notification.onclick = () => {
        this.view?.focus();
        this.zone.run(() => void this.router.navigate(['/conversations', alert.conversationId]));
        notification.close();
      };
    } catch {
      // Some browsers refuse page-level notifications. An alert that cannot be
      // shown is the same as one the person did not ask for: nothing happens.
    }
  }
}
