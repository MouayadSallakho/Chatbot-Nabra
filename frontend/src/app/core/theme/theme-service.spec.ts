import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { FakeMediaMatcher, hostileStorage, memoryStorage } from '@app/testing/test-doubles';
import { ThemeService } from './theme-service';
import { THEME_ATTRIBUTE, THEME_STORAGE_KEY } from './theme';

/**
 * The theme is the one piece of state in this phase with real branching, so it
 * gets real tests: the interesting cases are not "does light work" but the two
 * that are easy to write backwards -- `system` must keep following the operating
 * system, and an explicit choice must stop following it.
 */
describe('ThemeService', () => {
  const DARK = MediaQueries.prefersDark;

  function setup(options: { osDark?: boolean; storage?: Storage } = {}) {
    const media = new FakeMediaMatcher({ [DARK]: options.osDark ?? false });
    const storage = options.storage ?? memoryStorage();

    TestBed.configureTestingModule({
      providers: [
        { provide: MEDIA_QUERY_FACTORY, useValue: media.factory },
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    const service = TestBed.inject(ThemeService);

    return { media, storage, service };
  }

  /** The attribute the palette is actually selected by. */
  function paintedTheme(): string | null {
    TestBed.tick();

    return TestBed.inject(DOCUMENT).documentElement.getAttribute(THEME_ATTRIBUTE);
  }

  describe('when nothing has been chosen', () => {
    it('follows the system', () => {
      const { service } = setup();

      expect(service.preference()).toBe('system');
    });

    it('resolves to light while the system is light', () => {
      const { service } = setup({ osDark: false });

      expect(service.resolved()).toBe('light');
    });

    it('resolves to dark while the system is dark', () => {
      const { service } = setup({ osDark: true });

      expect(service.resolved()).toBe('dark');
    });

    it('follows the system when it changes later', () => {
      const { service, media } = setup({ osDark: false });

      media.set(DARK, true);

      expect(service.resolved()).toBe('dark');

      media.set(DARK, false);

      expect(service.resolved()).toBe('light');
    });
  });

  describe('when a theme has been chosen explicitly', () => {
    it('forces light even though the system asks for dark', () => {
      const { service } = setup({ osDark: true });

      service.select('light');

      expect(service.resolved()).toBe('light');
    });

    it('forces dark even though the system asks for light', () => {
      const { service } = setup({ osDark: false });

      service.select('dark');

      expect(service.resolved()).toBe('dark');
    });

    it('ignores the system changing afterwards', () => {
      const { service, media } = setup({ osDark: false });

      service.select('light');
      media.set(DARK, true);

      expect(service.resolved()).toBe('light');
    });

    it('starts following the system again when system is chosen', () => {
      const { service, media } = setup({ osDark: true });

      service.select('light');
      service.select('system');

      expect(service.resolved()).toBe('dark');

      media.set(DARK, false);

      expect(service.resolved()).toBe('light');
    });
  });

  describe('persistence', () => {
    it('stores the choice', () => {
      const { service, storage } = setup();

      service.select('dark');

      expect(storage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    });

    it('restores a stored choice on startup', () => {
      const { service } = setup({ storage: memoryStorage({ [THEME_STORAGE_KEY]: 'dark' }) });

      expect(service.preference()).toBe('dark');
      expect(service.resolved()).toBe('dark');
    });

    it('ignores a value it does not recognise', () => {
      // The store is editable by hand, so a bad value must not put the interface
      // into a state the code does not model.
      const { service } = setup({ storage: memoryStorage({ [THEME_STORAGE_KEY]: 'neon' }) });

      expect(service.preference()).toBe('system');
    });

    it('still switches theme when storage refuses every operation', () => {
      // Private browsing and some enterprise policies make localStorage throw on
      // access rather than return null. Theme switching has to keep working; it
      // simply will not survive a reload.
      const { service } = setup({ storage: hostileStorage(), osDark: false });

      expect(service.preference()).toBe('system');

      service.select('dark');

      expect(service.resolved()).toBe('dark');
    });
  });

  describe('applying the theme', () => {
    it('marks the document with the resolved theme', () => {
      const { service } = setup({ osDark: false });

      expect(paintedTheme()).toBe('light');

      service.select('dark');

      expect(paintedTheme()).toBe('dark');
    });

    it('marks the document with light or dark, never with the preference', () => {
      // `data-theme="system"` would force every themed rule to also handle the
      // media query, which is exactly the duplication the token layer avoids.
      const { service } = setup({ osDark: true });

      service.select('system');

      expect(paintedTheme()).toBe('dark');
    });
  });
});
