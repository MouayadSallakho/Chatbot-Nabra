import {
  formatCurrency,
  formatDate,
  formatNumber,
  formatPercent,
  formatRelative,
} from './locale-format';
import { pluralCategory, pluralMessage } from './plural';

/**
 * The digit and calendar policy (localisation-arabic-rtl.md, 5.4): Western
 * digits and the Gregorian calendar in both languages, whatever the runtime's
 * regional defaults would pick. The expected strings are the ones measured in
 * Chrome 155 and Node 24 (ICU 78) in Phase 36.3a.
 */
const WESTERN_ONLY = /^[^٠-٩۰-۹]*$/;
const OCT_18_2026 = '2026-10-18T14:05:00Z';

describe('locale formatting', () => {
  it('writes Arabic numbers with Western digits and Arabic-free separators', () => {
    expect(formatNumber('ar', 1234567.5)).toBe('1,234,567.5');
    expect(formatNumber('en', 1234567.5)).toBe('1,234,567.5');
  });

  it('writes Arabic dates in the Gregorian calendar, with month names and Western digits', () => {
    const arabic = formatDate('ar', OCT_18_2026, 'date', 'UTC');

    expect(arabic).toBe('18 أكتوبر 2026');
    expect(arabic).toMatch(WESTERN_ONLY);
    expect(formatDate('en', OCT_18_2026, 'date', 'UTC')).toBe('Oct 18, 2026');
  });

  it('keeps dates readable, never the all-numeric Arabic medium style', () => {
    expect(formatDate('ar', OCT_18_2026, 'dateTime', 'UTC')).not.toContain('/');
    expect(formatDate('ar', OCT_18_2026, 'dateTime', 'UTC')).toMatch(WESTERN_ONLY);
    expect(formatDate('en', OCT_18_2026, 'monthYear', 'UTC')).toBe('October 2026');
    expect(formatDate('ar', OCT_18_2026, 'monthYear', 'UTC')).toBe('أكتوبر 2026');
  });

  it('returns nothing for a value that is not a date, rather than "Invalid Date"', () => {
    expect(formatDate('en', 'not a date')).toBe('');
  });

  it('formats money in the currency the data names, with Western digits', () => {
    expect(formatCurrency('en', 1234.5, 'USD')).toBe('$1,234.50');
    expect(formatCurrency('ar', 1234.5, 'USD')).toMatch(WESTERN_ONLY);
    expect(formatCurrency('ar', 1234.5, 'USD')).toContain('1,234.50');
  });

  it('formats percentages and relative times with Western digits in Arabic', () => {
    expect(formatPercent('ar', 0.42)).toContain('42');
    expect(formatPercent('ar', 0.42)).toMatch(WESTERN_ONLY);
    expect(formatRelative('ar', -3, 'minute')).toBe('قبل 3 دقائق');
    expect(formatRelative('en', -3, 'minute')).toBe('3 minutes ago');
  });
});

describe('plural categories', () => {
  it('uses all six Arabic categories', () => {
    const categories = [0, 1, 2, 3, 10, 11, 21, 57, 99, 100, 101, 102].map((n) =>
      pluralCategory('ar', n),
    );

    expect(categories).toEqual([
      'zero',
      'one',
      'two',
      'few',
      'few',
      'many',
      'many',
      'many',
      'many',
      'other',
      'other',
      'other',
    ]);
  });

  it('uses only one and other in English', () => {
    expect([0, 1, 2, 57].map((n) => pluralCategory('en', n))).toEqual([
      'other',
      'one',
      'other',
      'other',
    ]);
  });

  it('picks the whole sentence for the count', () => {
    const forms = {
      zero: 'zero',
      one: 'one',
      two: 'two',
      few: 'few',
      many: 'many',
      other: 'other',
    };

    expect(pluralMessage('ar', 57, forms)).toBe('many');
    expect(pluralMessage('en', 57, forms)).toBe('other');
  });
});
