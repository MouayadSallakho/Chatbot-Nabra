import {
  displayZone,
  formatInZone,
  formatLocalDate,
  isLocalMidnight,
  isolatedZone,
  localDate,
  nextLocalDate,
  supportedZones,
  todayIn,
  zoneLabel,
  zoneName,
  zonePhrase,
} from './time-zone';

/**
 * The workspace's time zone in words and dates (Phase 38.5, E2).
 *
 * UTC, a zone east of it with no daylight saving (Riyadh), and one with it
 * (Berlin, whose clocks go back on 25 October 2026). The Arabic words are
 * checked against the real catalogue in `arabic-render.spec.ts`.
 */
describe('time zone helpers', () => {
  const LRI = '⁦';
  const PDI = '⁩';

  it('names UTC on its own and any other zone by its isolated IANA id', () => {
    expect(zonePhrase('UTC')).toBe('in UTC');
    expect(zonePhrase('Asia/Riyadh')).toBe(`in ${LRI}Asia/Riyadh${PDI} time`);
    expect(zoneLabel('UTC')).toBe('UTC');
    expect(zoneLabel('Etc/UTC')).toBe('UTC');
    expect(zoneLabel('America/New_York')).toBe(`${LRI}America/New_York${PDI} time`);
    expect(isolatedZone('Asia/Riyadh')).toBe(`${LRI}Asia/Riyadh${PDI}`);
  });

  it('gives the local date an instant falls on in a zone', () => {
    // 21:30 UTC on 16 October is 00:30 on the 17th in Riyadh.
    expect(localDate('2026-10-16T21:30:00Z', 'UTC')).toBe('2026-10-16');
    expect(localDate('2026-10-16T21:30:00Z', 'Asia/Riyadh')).toBe('2026-10-17');
    expect(localDate('not a date', 'UTC')).toBe('');
  });

  it('finds local midnights across a clock change', () => {
    expect(isLocalMidnight('2026-10-16T21:00:00Z', 'Asia/Riyadh')).toBe(true);
    expect(isLocalMidnight('2026-10-16T21:00:00Z', 'UTC')).toBe(false);

    // Berlin: midnight is 22:00 UTC in summer time and 23:00 UTC after the 25th.
    expect(isLocalMidnight('2026-10-24T22:00:00Z', 'Europe/Berlin')).toBe(true);
    expect(isLocalMidnight('2026-10-25T23:00:00Z', 'Europe/Berlin')).toBe(true);
    expect(isLocalMidnight('2026-10-25T22:00:00Z', 'Europe/Berlin')).toBe(false);
    expect(localDate('2026-10-25T22:30:00Z', 'Europe/Berlin')).toBe('2026-10-25');
  });

  it('takes today from the zone, not from the browser', () => {
    const now = new Date('2026-11-11T22:15:00Z');

    expect(todayIn('UTC', now)).toBe('2026-11-11');
    expect(todayIn('Asia/Riyadh', now)).toBe('2026-11-12');
    expect(todayIn('America/Los_Angeles', now)).toBe('2026-11-11');
  });

  it('formats an instant as it reads in the zone', () => {
    expect(formatInZone('en', '2026-10-16T21:30:00Z', 'time', 'UTC')).toBe('9:30 PM');
    expect(formatInZone('en', '2026-10-16T21:30:00Z', 'time', 'Asia/Riyadh')).toBe('12:30 AM');
  });

  it('formats an instant in UTC when the browser does not know the zone, rather than throwing', () => {
    expect(displayZone('Mars/Olympus_Mons')).toBe('UTC');
    expect(displayZone('Asia/Riyadh')).toBe('Asia/Riyadh');
    expect(() =>
      formatInZone('en', '2026-10-16T21:30:00Z', 'time', 'Mars/Olympus_Mons'),
    ).not.toThrow();
  });

  it('writes a local date from its parts, in both languages, with Western digits', () => {
    expect(formatLocalDate('en', '2026-03-03', 'date')).toBe('Mar 3, 2026');
    expect(formatLocalDate('ar', '2026-03-03', 'date')).toMatch(/3.*2026/);
    expect(formatLocalDate('ar', '2026-03-03', 'date')).not.toMatch(/[٠-٩]/);
    expect(formatLocalDate('en', 'nonsense')).toBe('nonsense');
  });

  it('steps to the next calendar date, across a month and a clock change', () => {
    expect(nextLocalDate('2026-10-31')).toBe('2026-11-01');
    expect(nextLocalDate('2026-10-25')).toBe('2026-10-26');
  });

  it('lists the zones this browser knows, from Intl', () => {
    const zones = supportedZones();

    expect(zones.length).toBeGreaterThan(100);
    expect(zones).toContain('Asia/Riyadh');
  });

  describe('zoneName (Phase 39.6b, Phase 38 note 8)', () => {
    it("names a zone in the reader's language, the generic name first", () => {
      expect(zoneName('Asia/Riyadh', 'en')).toBe('Arabian Standard Time');
      expect(zoneName('America/New_York', 'en')).toBe('Eastern Time');
      expect(zoneName('Asia/Riyadh', 'ar')).toBe('التوقيت العربي الرسمي');
    });

    it('falls back to the long name where the generic one is only an offset', () => {
      expect(zoneName('UTC', 'en')).toBe('Coordinated Universal Time');
      expect(zoneName('UTC', 'ar')).toBe('التوقيت العالمي المنسق');
    });

    it('gives no name for a zone the browser can only say as an offset', () => {
      expect(zoneName('Etc/GMT+5', 'en')).toBeNull();
      expect(zoneName('Etc/GMT+5', 'ar')).toBeNull();
    });

    it('gives no name for a zone this browser does not know', () => {
      expect(zoneName('Mars/Olympus_Mons', 'en')).toBeNull();
    });
  });
});
