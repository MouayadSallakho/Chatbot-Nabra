import { physicalSide } from './launcher-position';

describe('physicalSide', () => {
  it('keeps the physical corners in either language', () => {
    expect(physicalSide('Left', 'en')).toBe('Left');
    expect(physicalSide('Left', 'ar')).toBe('Left');
    expect(physicalSide('Right', 'en')).toBe('Right');
    expect(physicalSide('Right', 'ar')).toBe('Right');
  });

  it('puts Start left on an English page and right on an Arabic one', () => {
    expect(physicalSide('Start', 'en')).toBe('Left');
    expect(physicalSide('Start', 'ar')).toBe('Right');
  });

  it('puts End right on an English page and left on an Arabic one', () => {
    expect(physicalSide('End', 'en')).toBe('Right');
    expect(physicalSide('End', 'ar')).toBe('Left');
  });
});
