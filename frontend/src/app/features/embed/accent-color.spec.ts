import {
  DEFAULT_ACCENT,
  applyAccent,
  checkAccent,
  contrastRatio,
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
