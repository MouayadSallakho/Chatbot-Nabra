import { readableForeground } from './accent-color';

describe('readableForeground', () => {
  it('puts white on a dark accent', () => {
    expect(readableForeground('#1d3557')).toBe('#ffffff');
  });

  it('puts near-black on a light accent', () => {
    // The case a fixed white label gets wrong: a pale yellow launcher with white
    // text is a launcher nobody can read.
    expect(readableForeground('#ffe066')).toBe('#111827');
  });

  it('chooses the better of the two for a mid-tone', () => {
    // Mid greens are the awkward band. Whichever it picks, it must be the one
    // with more contrast -- that is the whole claim this function makes.
    const chosen = readableForeground('#2e8b57');

    expect(['#ffffff', '#111827']).toContain(chosen);
    expect(chosen).toBe('#ffffff');
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
