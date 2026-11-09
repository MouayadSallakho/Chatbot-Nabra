import { TestBed } from '@angular/core/testing';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { InboxSignals } from './inbox-signals';
import { SoundPlayer } from './sound-player';
import { SoundSettings } from './sound-settings';
import { Sounds } from './sounds';

/** The always-loaded door: who gets the player, and that its absence is silence. */
describe('Sounds', () => {
  let player: {
    arm: ReturnType<typeof vi.fn>;
    request: ReturnType<typeof vi.fn>;
    unlock: ReturnType<typeof vi.fn>;
    test: ReturnType<typeof vi.fn>;
  };

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) {
      TestBed.tick();
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
  };

  let signals: { start: ReturnType<typeof vi.fn> };

  function create(stored: Record<string, string> = {}): Sounds {
    player = { arm: vi.fn(), request: vi.fn(), unlock: vi.fn(), test: vi.fn(async () => true) };
    signals = { start: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting() },
        { provide: LOCAL_STORAGE, useValue: memoryStorage(stored) },
        { provide: SoundPlayer, useValue: player },
        { provide: InboxSignals, useValue: signals },
      ],
    });

    return TestBed.inject(Sounds);
  }

  const grant = (code: string): Promise<void> =>
    TestBed.inject(PermissionService).load('t1', Promise.resolve([code]));

  it('prepares nothing for a member who cannot read conversations', async () => {
    create().start();
    await settle();

    expect(player.arm).not.toHaveBeenCalled();
  });

  it('prepares the player once a member can read conversations', async () => {
    create().start();
    await grant('conversations.view');
    await settle();

    expect(player.arm).toHaveBeenCalled();
  });

  it('starts what decides when a sound is due, for a member who can hear them', async () => {
    create().start();
    await grant('conversations.view');
    await settle();

    expect(signals.start).toHaveBeenCalled();
  });

  it('starts nothing for a member who cannot read conversations', async () => {
    create().start();
    await settle();

    expect(signals.start).not.toHaveBeenCalled();
  });

  it('prepares nothing with sounds and alerts both off, until one is turned on', async () => {
    create({ 'omniai.sounds': 'off' }).start();
    await grant('conversations.view');
    await settle();

    expect(player.arm).not.toHaveBeenCalled();

    TestBed.inject(SoundSettings).setEnabled(true);
    await settle();

    expect(player.arm).toHaveBeenCalled();
  });

  it('is silence, not an error, when the player cannot be had', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting() },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: SoundPlayer,
          useFactory: () => {
            throw new Error('no chunk');
          },
        },
      ],
    });

    await expect(TestBed.inject(Sounds).test()).resolves.toBe(false);
  });

  it('passes requests, unlocks and the test on to the player', async () => {
    const sounds = create();

    sounds.request({ sound: 'ai-failed' });
    sounds.unlock();
    await settle();

    expect(player.request).toHaveBeenCalledWith({ sound: 'ai-failed' });
    expect(player.unlock).toHaveBeenCalled();
    expect(await sounds.test()).toBe(true);
  });
});
