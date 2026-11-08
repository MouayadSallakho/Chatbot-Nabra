import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { DesktopAlerts } from './desktop-alerts';
import { DesktopAlert } from './sound-names';
import { NotificationAccess, SOUND_PLATFORM } from './sound-platform';
import { SoundSettings } from './sound-settings';

describe('DesktopAlerts', () => {
  const alert: DesktopAlert = {
    title: 'Visitor 7KQ2MX',
    body: 'Needs a person',
    conversationId: 'c1',
  };

  class FakeNotification {
    onclick: (() => void) | null = null;
    closed = false;

    constructor(
      readonly title: string,
      readonly options: NotificationOptions,
    ) {}

    close(): void {
      this.closed = true;
    }
  }

  let permission: NotificationPermission;
  let shown: FakeNotification[];
  let requests: number;
  let answer: NotificationPermission;
  let visible: boolean;
  let focused: boolean;

  function create(
    language: 'en' | 'ar' = 'en',
    notifications?: NotificationAccess | null,
  ): DesktopAlerts {
    const access: NotificationAccess = {
      permission: () => permission,
      request: async () => {
        requests++;
        permission = answer;

        return answer;
      },
      show: (title, options) => {
        const notification = new FakeNotification(title, options);

        shown.push(notification);

        return notification as unknown as Notification;
      },
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: APP_LANGUAGE, useValue: language },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: SOUND_PLATFORM,
          useValue: {
            notifications: notifications === undefined ? access : notifications,
            isVisible: () => visible,
            hasFocus: () => focused,
          },
        },
      ],
    });

    return TestBed.inject(DesktopAlerts);
  }

  beforeEach(() => {
    permission = 'default';
    answer = 'granted';
    shown = [];
    requests = 0;
    visible = false;
    focused = false;
  });

  it('asks the browser once, and turns on only when it agrees', async () => {
    const alerts = create();

    expect(await alerts.enable()).toBe('on');
    expect(requests).toBe(1);
    expect(TestBed.inject(SoundSettings).desktopAlerts()).toBe(true);
  });

  it('asks nothing when the browser has already agreed', async () => {
    permission = 'granted';

    expect(await create().enable()).toBe('on');
    expect(requests).toBe(0);
  });

  it('stays off when the person refuses', async () => {
    answer = 'denied';

    const alerts = create();

    expect(await alerts.enable()).toBe('refused');
    expect(TestBed.inject(SoundSettings).desktopAlerts()).toBe(false);
    expect(alerts.blocked()).toBe(true);
  });

  it('stays off when the prompt is dismissed', async () => {
    answer = 'default';

    expect(await create().enable()).toBe('refused');
  });

  it('says so where the browser has no notifications', async () => {
    const alerts = create('en', null);

    expect(alerts.supported).toBe(false);
    expect(await alerts.enable()).toBe('unsupported');
  });

  describe('showing one', () => {
    async function turnedOn(language: 'en' | 'ar' = 'en'): Promise<DesktopAlerts> {
      permission = 'granted';

      const alerts = create(language);

      await alerts.enable();

      return alerts;
    }

    it('says who and why, tagged with the conversation, in the page language', async () => {
      (await turnedOn()).show(alert);

      expect(shown).toHaveLength(1);
      expect(shown[0].title).toBe('Visitor 7KQ2MX');
      expect(shown[0].options).toEqual({
        body: 'Needs a person',
        tag: 'c1',
        lang: 'en',
        dir: 'ltr',
      });
    });

    it('is right to left in Arabic', async () => {
      (await turnedOn('ar')).show(alert);

      expect(shown[0].options.lang).toBe('ar');
      expect(shown[0].options.dir).toBe('rtl');
    });

    it('stays quiet while the person is looking', async () => {
      visible = true;
      focused = true;
      (await turnedOn()).show(alert);

      expect(shown).toHaveLength(0);
    });

    it('still shows in a visible window without the focus', async () => {
      visible = true;
      focused = false;
      (await turnedOn()).show(alert);

      expect(shown).toHaveLength(1);
    });

    it('shows nothing while the setting is off', async () => {
      permission = 'granted';
      create().show(alert);

      expect(shown).toHaveLength(0);
    });

    it('shows nothing once the browser has withdrawn permission', async () => {
      const alerts = await turnedOn();

      permission = 'denied';
      alerts.show(alert);

      expect(shown).toHaveLength(0);
    });

    it('opens the conversation and closes itself when clicked', async () => {
      const alerts = await turnedOn();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

      alerts.show(alert);
      shown[0].onclick?.();

      expect(navigate).toHaveBeenCalledWith(['/conversations', 'c1']);
      expect(shown[0].closed).toBe(true);
    });

    it('never throws when the browser refuses to show one', async () => {
      permission = 'granted';

      const alerts = create('en', {
        permission: () => 'granted',
        request: async () => 'granted',
        show: () => {
          throw new TypeError('Illegal constructor');
        },
      });

      await alerts.enable();

      expect(() => alerts.show(alert)).not.toThrow();
    });
  });
});
