import {
  answeredBefore,
  answeredCounts,
  answeredShare,
  channelShares,
  dayPart,
  greeting,
  todayWords,
} from './overview-words';

/** The overview's own words (Phase 39.7a), in both languages. */
describe('overview words', () => {
  it('splits the day at 05:00, 12:00 and 18:00', () => {
    expect([4, 5, 11, 12, 17, 18, 23].map(dayPart)).toEqual([
      'evening',
      'morning',
      'morning',
      'afternoon',
      'afternoon',
      'evening',
      'evening',
    ]);
  });

  it('greets by name, the name isolated so it keeps its own direction', () => {
    expect(greeting('afternoon', 'Rana Haddad')).toBe('Good afternoon, ⁨Rana Haddad⁩');
    // An Arabic name inside an English sentence, and the other way round.
    expect(greeting('morning', 'عمر صالح')).toBe('Good morning, ⁨عمر صالح⁩');
  });

  it('greets without a name rather than with an email or an empty comma', () => {
    expect(greeting('evening', null)).toBe('Good evening');
    expect(greeting('evening', '   ')).toBe('Good evening');
  });

  it("writes the reader's day with Latin digits and the Gregorian calendar in both languages", () => {
    const day = new Date(2026, 10, 15, 14, 0);

    expect(todayWords('en', day)).toBe('Sunday, November 15');
    expect(todayWords('ar', day)).toContain('الأحد');
    expect(todayWords('ar', day)).toContain('15');
    expect(todayWords('ar', day)).toContain('نوفمبر');
    expect(todayWords('ar', day)).not.toMatch(/[٠-٩]/);
  });

  it('gives no share of nothing: a period with no conversation is not 0%', () => {
    expect(answeredShare(0, 0)).toBeNull();
    expect(answeredShare(9, 12)).toBe(0.75);
    // Never more than the whole, whatever arrives.
    expect(answeredShare(13, 12)).toBe(1);
  });

  it('says the two counts behind the ring, formatted per language', () => {
    expect(answeredCounts(1002, 1284, 'en')).toBe('1,002 of 1,284');
    expect(answeredCounts(1002, 1284, 'ar')).toBe('1,002 of 1,284');
  });

  it("says the previous period's share, or why there is none", () => {
    expect(answeredBefore(6, 8, true, 'en')).toBe('The 7 days before: 75%');
    expect(answeredBefore(0, 0, true, 'en')).toBe('No conversations started the 7 days before');
    expect(answeredBefore(0, 0, false, 'en')).toBe('No earlier data to compare');
    // Arabic keeps Latin digits; the percent sign carries its direction marks.
    expect(answeredBefore(6, 8, true, 'ar')).toContain('75');
    expect(answeredBefore(6, 8, true, 'ar')).not.toMatch(/[٠-٩]/);
  });

  it('shares the channels out of their own counts, largest first, empty ones left out', () => {
    expect(
      channelShares([
        { channel: 'WhatsApp', conversationsStarted: 3 },
        { channel: 'Instagram', conversationsStarted: 0 },
        { channel: 'WebChat', conversationsStarted: 9 },
      ]),
    ).toEqual([
      { channel: 'WebChat', count: 9, share: 0.75 },
      { channel: 'WhatsApp', count: 3, share: 0.25 },
    ]);
    expect(channelShares([{ channel: 'WebChat', conversationsStarted: 0 }])).toEqual([]);
  });
});
