// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';
import {
  BOUNDARY_CONTRAST,
  CHAT_GROUNDS,
  DEFAULT_ACCENT,
  applyAccent,
  applyChatBrand,
  chatBrand,
  checkAccent,
  checkChatBrand,
  contrastRatio,
  pageAccent,
  readableForeground,
} from './accent-color';

describe('readableForeground', () => {
  it('puts white on a dark accent', () => {
    expect(readableForeground('#1d3557')).toBe('#ffffff');
  });

  it('puts near-black on a light accent', () => {
    // The case a fixed white label gets wrong: a pale yellow launcher with white
    // text is a launcher nobody can read.
    expect(readableForeground('#ffe066')).toBe('#111827');
  });

  it('falls back to black for a mid-tone where neither usual label reaches 4.5:1', () => {
    // Sea green is the awkward band: white is 4.25:1 and near-black 4.17:1 on it.
    // Before Phase 36.9 the widget took the better of the two and shipped a
    // label that failed AA.
    expect(contrastRatio('#2e8b57', '#ffffff')).toBeLessThan(4.5);
    expect(contrastRatio('#2e8b57', '#111827')).toBeLessThan(4.5);
    expect(readableForeground('#2e8b57')).toBe('#000000');
  });

  it('reaches 4.5:1 on every colour a workspace can choose', () => {
    // Every 17th step of each channel: 4,096 colours across the whole cube.
    for (let r = 0; r <= 255; r += 17) {
      for (let g = 0; g <= 255; g += 17) {
        for (let b = 0; b <= 255; b += 17) {
          const accent = '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('');

          expect(contrastRatio(accent, readableForeground(accent))).toBeGreaterThanOrEqual(4.5);
        }
      }
    }
  });

  it('falls back to white for a value that is not a hex colour', () => {
    // The server stores only expanded lower-case six-digit hex, so this is
    // defence against a response that could not have come from it.
    for (const junk of ['', 'red', '#fff', 'rgb(0 0 0)', 'javascript:alert(1)']) {
      expect(readableForeground(junk)).toBe('#ffffff');
    }
  });

  it('reads the same colour written either case', () => {
    expect(readableForeground('#FFE066')).toBe(readableForeground('#ffe066'));
  });
});

describe('checkAccent', () => {
  it('replaces anything that is not a colour with the default before it is used', () => {
    for (const junk of ['', 'red;background:url(x)', '#fff', 'var(--x)']) {
      expect(checkAccent(junk).accent).toBe(DEFAULT_ACCENT);
    }
  });

  it('outlines a launcher too pale to see on a white page, in its own label colour', () => {
    const pale = checkAccent('#ffe066');

    expect(pale.onLightPage).toBeLessThan(3);
    expect(pale.edge).toBe(pale.foreground);
    expect(contrastRatio(pale.edge!, '#ffffff')).toBeGreaterThanOrEqual(3);
  });

  it('draws no outline when the accent already stands out on white', () => {
    expect(checkAccent(DEFAULT_ACCENT).edge).toBeNull();
  });

  it('reports the label contrast it guarantees', () => {
    expect(checkAccent('#2e8b57').labelContrast).toBeGreaterThanOrEqual(4.5);
  });
});

describe('pageAccent', () => {
  it('takes an exact #RRGGBB and picks the ink that reads on it', () => {
    expect(pageAccent('#1D3557')).toEqual({ accent: '#1d3557', foreground: '#ffffff' });
    expect(pageAccent('#ffe066')).toEqual({ accent: '#ffe066', foreground: '#111827' });
  });

  it('ignores everything that is not exactly #RRGGBB', () => {
    for (const invalid of [
      '',
      ' #1d3557',
      '#1d3557 ',
      '#1d3557\n',
      '#fff',
      '#1d35577',
      '#1d3557ff',
      '1d3557',
      'red',
      'var(--x)',
      'url(x)',
      'red;background:url(x)',
      '#1d355g',
      null,
      undefined,
      42,
      {},
    ]) {
      expect(pageAccent(invalid)).toBeNull();
    }
  });

  it('keeps the product look on a colour neither ink reaches 4.5:1 on', () => {
    // White is 4.25:1 and near-black 4.17:1 on sea green.
    expect(pageAccent('#2e8b57')).toBeNull();
  });

  it('never returns a pair under 4.5:1, across the whole colour cube', () => {
    for (let r = 0; r <= 255; r += 17) {
      for (let g = 0; g <= 255; g += 17) {
        for (let b = 0; b <= 255; b += 17) {
          const value = '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('');
          const accent = pageAccent(value);

          if (accent) {
            expect(contrastRatio(accent.accent, accent.foreground)).toBeGreaterThanOrEqual(4.5);
          }
        }
      }
    }
  });
});

describe('applyAccent', () => {
  it('sets only custom properties, from the checked value', () => {
    const element = document.createElement('div');

    applyAccent(element, '#FFE066');

    expect(element.style.getPropertyValue('--chat-accent')).toBe('#ffe066');
    expect(element.style.getPropertyValue('--chat-on-accent')).toBe('#111827');
    expect(element.style.getPropertyValue('--chat-accent-edge')).toBe('#111827');

    applyAccent(element, '#1d3557');

    expect(element.style.getPropertyValue('--chat-accent-edge')).toBe('');
  });
});

/**
 * The workspace's colour inside the chat (Calm Glass, Phase 39.5b): it stands in
 * for emerald on the tile and Send only in a theme where every pair holds.
 */
describe('checkChatBrand and chatBrand', () => {
  it('keeps the grounds equal to the tokens they stand for', () => {
    const tokens: string = readFileSync('src/styles/_tokens.scss', 'utf8');
    const raw = (name: string): string =>
      new RegExp(`--${name}:\\s*(#[0-9a-f]{3,6})`, 'i').exec(tokens)?.[1]?.toLowerCase() ?? '';
    const expand = (hex: string): string =>
      hex.length === 4 ? '#' + [...hex.slice(1)].map((c) => c + c).join('') : hex;

    expect(CHAT_GROUNDS.light).toEqual([raw('cream-50'), expand(raw('white'))]);
    expect(CHAT_GROUNDS.dark).toEqual([raw('emerald-950'), raw('emerald-900')]);
  });

  it('uses a navy in the light look and keeps emerald in the dark one', () => {
    expect(checkChatBrand('#1d3557', 'light').used).toBe(true);
    expect(checkChatBrand('#1d3557', 'dark').used).toBe(false);
    expect(chatBrand('#1d3557')).toEqual({
      light: { accent: '#1d3557', foreground: '#ffffff' },
      dark: null,
    });
  });

  it('uses a pale yellow in the dark look only, with the dark ink', () => {
    expect(chatBrand('#FFE066')).toEqual({
      light: null,
      dark: { accent: '#ffe066', foreground: '#111827' },
    });
  });

  it('keeps emerald in both looks for a mid-tone no ink reads on at 4.5:1', () => {
    expect(checkChatBrand('#7a7a7a', 'light').onGround).toBeGreaterThanOrEqual(BOUNDARY_CONTRAST);
    expect(chatBrand('#7a7a7a')).toEqual({ light: null, dark: null });
  });

  it.each([undefined, null, 42, 'red', '#fff', ' #1d3557', 'url(x)'])(
    'never lets %s through',
    (value) => {
      expect(chatBrand(value)).toEqual({ light: null, dark: null });
      expect(checkChatBrand(value, 'light').used).toBe(false);
    },
  );

  it('proves every pair for each colour it lets through, in both looks', () => {
    for (let r = 0; r <= 255; r += 51) {
      for (let g = 0; g <= 255; g += 51) {
        for (let b = 0; b <= 255; b += 51) {
          const value = '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('');
          const brand = chatBrand(value);

          for (const theme of ['light', 'dark'] as const) {
            const accent = brand[theme];

            if (accent) {
              expect(contrastRatio(accent.accent, accent.foreground)).toBeGreaterThanOrEqual(4.5);

              for (const ground of CHAT_GROUNDS[theme]) {
                expect(contrastRatio(accent.accent, ground)).toBeGreaterThanOrEqual(3);
              }
            }
          }
        }
      }
    }
  });
});

describe('applyChatBrand', () => {
  it('sets the properties of each look the colour holds in, and clears the others', () => {
    const element = document.createElement('div');

    applyChatBrand(element, chatBrand('#ffe066'));

    expect(element.style.getPropertyValue('--chat-brand-dark')).toBe('#ffe066');
    expect(element.style.getPropertyValue('--chat-on-brand-dark')).toBe('#111827');
    expect(element.style.getPropertyValue('--chat-brand-light')).toBe('');

    applyChatBrand(element, chatBrand('#1d3557'));

    expect(element.style.getPropertyValue('--chat-brand-light')).toBe('#1d3557');
    expect(element.style.getPropertyValue('--chat-brand-dark')).toBe('');
    expect(element.style.getPropertyValue('--chat-on-brand-dark')).toBe('');
  });
});
