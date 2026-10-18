import { isApplePlatform, isPaletteShortcut, shortcutAria, shortcutLabel } from './shortcut';

const key = (init: KeyboardEventInit) => new KeyboardEvent('keydown', init);

describe('palette shortcut', () => {
  it('is Ctrl+K on Windows and Linux', () => {
    expect(isPaletteShortcut(key({ key: 'k', code: 'KeyK', ctrlKey: true }), false)).toBe(true);
    expect(isPaletteShortcut(key({ key: 'k', code: 'KeyK', metaKey: true }), false)).toBe(false);
    expect(shortcutLabel(false)).toBe('Ctrl K');
    expect(shortcutAria(false)).toBe('Control+K');
  });

  it('is Command+K on Apple devices, leaving Control+K to text editing', () => {
    expect(isPaletteShortcut(key({ key: 'k', code: 'KeyK', metaKey: true }), true)).toBe(true);
    expect(isPaletteShortcut(key({ key: 'k', code: 'KeyK', ctrlKey: true }), true)).toBe(false);
    expect(shortcutLabel(true)).toBe('⌘K');
    expect(shortcutAria(true)).toBe('Meta+K');
  });

  it('works with an Arabic keyboard layout, where the K key types ن', () => {
    expect(isPaletteShortcut(key({ key: 'ن', code: 'KeyK', ctrlKey: true }), false)).toBe(true);
  });

  it('ignores the combination with Shift or Alt, and other keys', () => {
    expect(
      isPaletteShortcut(key({ key: 'K', code: 'KeyK', ctrlKey: true, shiftKey: true }), false),
    ).toBe(false);
    expect(
      isPaletteShortcut(key({ key: 'k', code: 'KeyK', ctrlKey: true, altKey: true }), false),
    ).toBe(false);
    expect(isPaletteShortcut(key({ key: 'j', code: 'KeyJ', ctrlKey: true }), false)).toBe(false);
    expect(isPaletteShortcut(key({ key: 'k', code: 'KeyK' }), false)).toBe(false);
  });

  it('recognises Apple platforms', () => {
    expect(isApplePlatform({ platform: 'MacIntel', userAgent: '' })).toBe(true);
    expect(
      isApplePlatform({ platform: '', userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0)' }),
    ).toBe(true);
    expect(isApplePlatform({ platform: 'Win32', userAgent: 'Mozilla/5.0 (Windows NT 10.0)' })).toBe(
      false,
    );
    expect(isApplePlatform(undefined)).toBe(false);
  });
});
