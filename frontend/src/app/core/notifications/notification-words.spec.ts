import { AppNotification } from './notification-api';
import { notificationText } from './notification-words';

describe('notificationText', () => {
  const base: AppNotification = {
    id: 'n1',
    kind: 'ReportCompleted',
    category: 'Reports',
    severity: 'Information',
    title: 'Your report is ready',
    body: 'Your “Conversation activity” report has finished generating and is ready to download.',
    actionPath: '/reports',
    createdAt: '2026-10-19T09:00:00Z',
    readAt: null,
  };

  it('says a known kind in the client’s own words', () => {
    const text = notificationText({ ...base, title: 'server title' }, 'en');

    expect(text.title).toBe('Your report is ready');
  });

  it('keeps the report’s name from the server’s sentence, as the server wrote it', () => {
    expect(notificationText(base, 'en').body).toBe(
      'Your “Conversation activity” report has finished generating and is ready to download.',
    );
  });

  it('says the sentence without a name rather than guessing one', () => {
    const text = notificationText({ ...base, body: 'Your report is ready.' }, 'en');

    expect(text.body).toBe('Your report has finished generating and is ready to download.');
  });

  it('says the usage threshold from the kind, formatted for the reader', () => {
    const text = notificationText(
      { ...base, kind: 'UsageCritical', category: 'Usage', body: 'anything' },
      'en',
    );

    expect(text.title).toBe('AI usage has reached 90% of the monthly limit');
  });

  it('keeps the server’s words for a kind this build does not know', () => {
    const text = notificationText(
      { ...base, kind: 'SomethingNewer', title: 'A newer title', body: 'A newer body.' },
      'en',
    );

    expect(text).toEqual({ title: 'A newer title', body: 'A newer body.' });
  });
});
