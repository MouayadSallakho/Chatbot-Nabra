import { AnalyticsDashboard } from './analytics-api';
import {
  dailyPoints,
  conversationActivity,
  dataSinceSentence,
  failedRepliesTitle,
  figureChange,
  localDay,
  messagesSummary,
  periodSentence,
  repliesSummary,
} from './analytics-words';

/**
 * The numbers' words: days are the workspace's local days, the running day is
 * marked in the workspace's zone, and sentences come from real counts.
 */
describe('analytics words', () => {
  const dashboard = {
    period: {
      range: '7d',
      from: '2026-10-17T00:00:00+00:00',
      to: '2026-10-18T19:27:49+00:00',
      days: 2,
      timeZone: 'UTC',
    },
    summary: {
      conversationsStarted: 3,
      customerMessages: 5,
      aiMessages: 2,
      humanMessages: 1,
      systemMessages: 0,
      conversationsAnsweredByAi: 0,
    },
    dailyActivity: [
      {
        date: '2026-10-17',
        conversationsStarted: 1,
        customerMessages: 1,
        aiMessages: 0,
        humanMessages: 0,
      },
      {
        date: '2026-10-18',
        conversationsStarted: 2,
        customerMessages: 4,
        aiMessages: 2,
        humanMessages: 1,
      },
    ],
  } as unknown as AnalyticsDashboard;

  it('pairs the same local day offset and keeps the previous date in the accessible label', () => {
    const previous = dashboard.dailyActivity.map((day, i) => ({
      ...day,
      date: '2026-10-' + (15 + i),
      conversationsStarted: i * 4,
    }));
    const data = {
      ...dashboard,
      previous: {
        period: dashboard.period,
        summary: dashboard.summary,
        aiOperations: dashboard.aiOperations,
        dailyActivity: previous,
      },
    };
    const chart = conversationActivity(data, 'en');
    expect(chart.points.map((day) => day.values['previous'])).toEqual([0, 4]);
    expect(chart.points[0].longLabel).toContain('October 15, 2026');
    expect(conversationActivity(dashboard, 'en').series).toHaveLength(1);
  });

  it('writes a bucket date as the day it is, whatever the browser zone', () => {
    expect(localDay('2026-09-15', 'en', 'longDate')).toBe('September 15, 2026');
    expect(localDay('not-a-date', 'en')).toBe('not-a-date');
  });

  it('says the zone the days are counted in, and the time of the read there', () => {
    expect(periodSentence(dashboard, 'en')).toBe(
      'Oct 17, 2026 – Oct 18, 2026, whole days in UTC. Today is counted until 7:27 PM.',
    );

    const riyadh = {
      ...dashboard,
      period: { ...dashboard.period, timeZone: 'Asia/Riyadh' },
    } as AnalyticsDashboard;

    // 19:27 UTC is 22:27 in Riyadh; the id is isolated so it keeps its order in Arabic.
    expect(periodSentence(riyadh, 'en')).toBe(
      'Oct 17, 2026 – Oct 18, 2026, whole days in ⁦Asia/Riyadh⁩ time. Today is counted until 10:27 PM.',
    );
  });

  it('marks the running day in the workspace zone, not by the UTC date of the read', () => {
    // 22:30 UTC on the 18th is already the 19th in Riyadh: no bucket is today there.
    const late = {
      ...dashboard,
      period: { ...dashboard.period, to: '2026-10-18T22:30:00+00:00', timeZone: 'Asia/Riyadh' },
    } as AnalyticsDashboard;

    expect(dailyPoints(late, 'en').map((point) => point.partial)).toEqual([false, false]);

    // Berlin in summer time: 22:00 UTC is midnight there, so this period has ended.
    const berlin = {
      ...dashboard,
      period: { ...dashboard.period, to: '2026-10-17T22:00:00+00:00', timeZone: 'Europe/Berlin' },
    } as AnalyticsDashboard;

    expect(dailyPoints(berlin, 'en').some((point) => point.partial)).toBe(false);
  });

  it('words a change against the previous period, never as a bare colour', () => {
    const running = { soFar: true, earlierData: true };
    const ended = { soFar: false, earlierData: true };

    expect(figureChange(12, 10, running, 'en')).toEqual({
      direction: 'up',
      glyph: '↑',
      text: 'So far, up 20% on the previous period',
    });
    expect(figureChange(9, 10, ended, 'en').text).toBe('Down 10% on the previous period');
    expect(figureChange(1003, 1000, ended, 'en').text).toBe('Up 0.3% on the previous period');
    expect(figureChange(5, 5, ended, 'en')).toEqual({
      direction: 'same',
      glyph: '=',
      text: 'The same as the previous period',
    });
    expect(figureChange(5, 0, running, 'en')).toEqual({
      direction: 'none',
      glyph: '',
      text: 'None in the previous period',
    });
    expect(figureChange(5, 0, { soFar: true, earlierData: false }, 'en').text).toBe(
      'No earlier data to compare',
    );
  });

  it('says how far back the data goes, or that there is none yet', () => {
    expect(dataSinceSentence('2026-03-03', 'en')).toBe('Data since Mar 3, 2026.');
    expect(dataSinceSentence(null, 'en')).toBe(
      'No conversation has started in this workspace yet.',
    );
  });

  it('marks today as still running, and only while it is', () => {
    const points = dailyPoints(dashboard, 'en');

    expect(points.map((point) => point.partial)).toEqual([false, true]);
    expect(points[1].values).toEqual({ customer: 4, human: 1, ai: 2, conversations: 2 });

    const closed = dailyPoints(
      { ...dashboard, period: { ...dashboard.period, to: '2026-10-19T00:00:00+00:00' } },
      'en',
    );

    expect(closed.some((point) => point.partial)).toBe(false);
  });

  it('summarises the period from the counts it was given', () => {
    expect(messagesSummary(dashboard, 'en')).toBe(
      'Messages in this period. Customers: 5, team: 1, AI: 2. Busiest day: October 18, 2026 (7).',
    );
    expect(repliesSummary(2, 1, 'en')).toContain('The AI wrote 67% of them.');
    expect(repliesSummary(0, 0, 'en')).toBe('Nobody replied to a customer in this period.');
  });

  it('counts with the plural form the language asks for', () => {
    expect(failedRepliesTitle(1, 'en')).toBe('1 AI reply failed');
    expect(failedRepliesTitle(106, 'en')).toBe('106 AI replies failed');
  });
});
