import { DOCUMENT, InjectionToken, inject } from '@angular/core';

/** The browser's notifications, in the three operations this application uses. */
export interface NotificationAccess {
  permission(): NotificationPermission;
  request(): Promise<NotificationPermission>;
  show(title: string, options: NotificationOptions): Notification;
}

/**
 * Every browser feature the sounds touch, each of which can be missing.
 *
 * Behind one token so a test supplies fakes for all of them, and so "this browser
 * does not have it" is an ordinary `null` for the code that uses it.
 */
export interface SoundPlatform {
  createAudioContext(): AudioContext | null;
  fetch(url: string): Promise<Response>;
  locks: LockManager | null;
  createChannel(name: string): BroadcastChannel | null;
  notifications: NotificationAccess | null;

  /** Whether the page is on screen. */
  isVisible(): boolean;

  /** Whether the page has the keyboard focus. */
  hasFocus(): boolean;

  /** Whether the browser counts this moment as the person's own gesture. */
  isUserActive(): boolean;
}

export const SOUND_PLATFORM = new InjectionToken<SoundPlatform>('SOUND_PLATFORM', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    const view = document.defaultView;
    const NotificationClass = view?.Notification;

    return {
      createAudioContext: () => (view?.AudioContext ? new view.AudioContext() : null),
      fetch: (url) => (view as Window).fetch(url),
      locks: view?.navigator.locks ?? null,
      createChannel: (name) => (view?.BroadcastChannel ? new view.BroadcastChannel(name) : null),
      notifications: NotificationClass
        ? {
            permission: () => NotificationClass.permission,
            request: () => NotificationClass.requestPermission(),
            show: (title, options) => new NotificationClass(title, options),
          }
        : null,
      isVisible: () => document.visibilityState === 'visible',
      hasFocus: () => document.hasFocus(),
      // Browsers without the API have no way to say; the attempt is harmless there.
      isUserActive: () => view?.navigator.userActivation?.isActive ?? true,
    };
  },
});
