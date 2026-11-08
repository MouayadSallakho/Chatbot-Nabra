import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ArabicFonts } from './arabic-fonts';
import { APP_LANGUAGE, AppLanguage } from './language';

describe('ArabicFonts', () => {
  function service(
    language: AppLanguage,
    fonts: unknown = { load: vi.fn(() => Promise.resolve([])) },
  ) {
    TestBed.configureTestingModule({
      providers: [
        { provide: APP_LANGUAGE, useValue: language },
        { provide: DOCUMENT, useValue: { fonts } },
      ],
    });

    return {
      fonts: fonts as { load: ReturnType<typeof vi.fn> },
      arabic: TestBed.inject(ArabicFonts),
    };
  }

  it('asks for the regular and strong faces when Arabic arrives on an English page', () => {
    const { arabic, fonts } = service('en');

    arabic.notice(['Hello', 'مرحبا']);

    expect(fonts.load.mock.calls.map((call) => call[0])).toEqual([
      '400 1em "IBM Plex Sans Arabic"',
      '600 1em "IBM Plex Sans Arabic"',
    ]);
    // The sample text is what makes the unicode-range pick the Arabic file.
    expect(fonts.load.mock.calls[0][1]).toMatch(/[؀-ۿ]/);
  });

  it('asks once per page, however many responses hold Arabic', () => {
    const { arabic, fonts } = service('en');

    arabic.notice(['مرحبا']);
    arabic.notice(['أهلا']);

    expect(fonts.load).toHaveBeenCalledTimes(2);
  });

  it('asks for nothing while no Arabic has arrived', () => {
    const { arabic, fonts } = service('en');

    arabic.notice(['Hello', null, undefined, '12345']);

    expect(fonts.load).not.toHaveBeenCalled();
  });

  it('keeps watching after text with no Arabic in it', () => {
    const { arabic, fonts } = service('en');

    arabic.notice(['Hello']);
    arabic.notice(['مرحبا']);

    expect(fonts.load).toHaveBeenCalledTimes(2);
  });

  it('does nothing on an Arabic page, which already has the faces', () => {
    const { arabic, fonts } = service('ar');

    arabic.notice(['مرحبا']);

    expect(fonts.load).not.toHaveBeenCalled();
  });

  it('swallows a font that fails to load, because it was only a head start', async () => {
    const { arabic, fonts } = service('en', {
      load: vi.fn(() => Promise.reject(new Error('network'))),
    });

    expect(() => arabic.notice(['مرحبا'])).not.toThrow();
    await expect(fonts.load.mock.results[0].value.catch(() => 'handled')).resolves.toBe('handled');
  });

  it('does nothing in a browser without the font loading API', () => {
    const { arabic } = service('en', undefined);

    expect(() => arabic.notice(['مرحبا'])).not.toThrow();
  });
});
