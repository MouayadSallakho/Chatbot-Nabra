import { AnalyticsDashboard } from './analytics-api';
import {
  dailyPoints,
  failedRepliesTitle,
  messagesSummary,
  repliesSummary,
  utcDay,
} from './analytics-words';

/** The numbers' words: days read as UTC days, the running day marked, sentences from real counts. */
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

  it('reads a bucket date as the UTC day it is', () => {
    expect(utcDay('2026-09-15', 'en', 'longDate')).toBe('September 15, 2026');
    expect(utcDay('not-a-date', 'en')).toBe('not-a-date');
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
