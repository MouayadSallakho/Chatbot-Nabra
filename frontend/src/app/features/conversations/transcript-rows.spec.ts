import type { TranscriptEvent, TranscriptMessage } from './conversations-api';
import {
  REFRESH_OVERLAP,
  mergeEvents,
  mergeMessages,
  refreshCursor,
  transcriptRows,
} from './transcript-rows';

const event = (
  id: string,
  kind: string,
  afterSequence: number,
  at = '2026-10-18T10:00:00Z',
): TranscriptEvent => ({
  id,
  kind,
  at,
  afterSequence,
  actor: kind === 'AiTurnFailed' ? null : { userId: 'u1', email: 'sam@acme.test' },
  reason: kind === 'AiTurnFailed' ? 'generation_failed' : null,
});

describe('transcriptRows', () => {
  const message = (
    id: string,
    sender: TranscriptMessage['sender'],
    sentAt: string,
    sequence: number,
  ): TranscriptMessage => ({ id, sender, text: id, sentAt, sequence, sentBy: null });

  // Local noon, so no time zone the runner sits in can move these days.
  const now = new Date(2026, 9, 18, 12, 0, 0);
  const at = (day: number, hour = 10) => new Date(2026, 9, day, hour, 0, 0).toISOString();

  const kinds = (rows: ReturnType<typeof transcriptRows>) =>
    rows.map((row) =>
      row.kind === 'message'
        ? row.key
        : row.kind === 'day'
          ? `day:${row.label}`
          : `event:${row.event.id}`,
    );

  it('puts a day separator before the first message of each day, and names today and yesterday', () => {
    const rows = transcriptRows(
      [
        message('a', 'Customer', at(16), 1),
        message('b', 'Customer', at(17), 2),
        message('c', 'Customer', at(17, 11), 3),
        message('d', 'Customer', at(18), 4),
      ],
      [],
      'en',
      now,
    );

    const days = rows
      .filter((row) => row.kind === 'day')
      .map((row) => row.kind === 'day' && row.label);

    expect(days).toEqual(['October 16, 2026', 'Yesterday', 'Today']);
    expect(kinds(rows)).toEqual([
      'day:October 16, 2026',
      'a',
      'day:Yesterday',
      'b',
      'c',
      'day:Today',
      'd',
    ]);
  });

  it('places each event after the message whose sequence it names', () => {
    const rows = transcriptRows(
      [
        message('c1', 'Customer', at(18), 10),
        message('ai1', 'AiAgent', at(18), 11),
        message('c2', 'Customer', at(18), 12),
      ],
      [event('failed', 'AiTurnFailed', 10), event('took', 'TakenOver', 12)],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual(['day:Today', 'c1', 'event:failed', 'ai1', 'c2', 'event:took']);
  });

  it('keeps an event with the day of the message it follows', () => {
    const rows = transcriptRows(
      [message('a', 'Customer', at(17), 1), message('b', 'Customer', at(18), 2)],
      [event('took', 'TakenOver', 1)],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual(['day:Yesterday', 'a', 'event:took', 'day:Today', 'b']);
  });

  it('infers nothing from a change of voice: no event, no divider', () => {
    const rows = transcriptRows(
      [
        message('c1', 'Customer', at(18), 1),
        message('ai1', 'AiAgent', at(18), 2),
        message('p1', 'HumanAgent', at(18), 3),
        message('ai2', 'AiAgent', at(18), 4),
      ],
      [],
      'en',
      now,
    );

    expect(rows.some((row) => row.kind === 'event')).toBe(false);
  });

  it('does not draw a kind of event this build cannot word', () => {
    const rows = transcriptRows(
      [message('c1', 'Customer', at(18), 1)],
      [event('new', 'SomethingNew', 1)],
      'en',
      now,
    );

    expect(rows.some((row) => row.kind === 'event')).toBe(false);
  });

  it('draws an event from before the first message at the top', () => {
    const rows = transcriptRows(
      [message('c1', 'Customer', at(18), 5)],
      [event('took', 'TakenOver', 0)],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual(['event:took', 'day:Today', 'c1']);
  });

  it('draws an event after the newest message at the end', () => {
    const rows = transcriptRows(
      [message('c1', 'Customer', at(18), 5)],
      [event('back', 'ReturnedToAi', 5)],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual(['day:Today', 'c1', 'event:back']);
  });

  it('keeps the order it was given, whatever the timestamps say', () => {
    const rows = transcriptRows(
      [message('later', 'Customer', at(18, 11), 1), message('earlier', 'Customer', at(18, 9), 2)],
      [],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual(['day:Today', 'later', 'earlier']);
  });

  it('marks which voice each message belongs to, and none for the customer', () => {
    const rows = transcriptRows(
      [
        message('c', 'Customer', at(18), 1),
        message('ai', 'AiAgent', at(18), 2),
        message('p', 'HumanAgent', at(18), 3),
      ],
      [],
      'en',
      now,
    );

    const voices = rows.flatMap((row) => (row.kind === 'message' ? [row.voice] : []));

    expect(voices).toEqual([null, 'ai', 'human']);
  });
});

describe('mergeMessages', () => {
  const message = (sequence: number, text = `m${sequence}`): TranscriptMessage => ({
    id: `id-${sequence}`,
    sender: 'Customer',
    text,
    sentAt: '2026-10-18T10:00:00Z',
    sequence,
    sentBy: null,
  });

  it('adds new messages without duplicating the ones on screen, in sequence order', () => {
    const merged = mergeMessages(
      [message(1), message(2)],
      [message(2, 'again'), message(4), message(3)],
    );

    expect(merged.map((m) => m.sequence)).toEqual([1, 2, 3, 4]);
    expect(merged[1].text).toBe('again');
  });
});

describe('mergeEvents', () => {
  it('keeps an event served by two pages once', () => {
    const merged = mergeEvents([event('e1', 'TakenOver', 10)], [event('e1', 'TakenOver', 10)]);

    expect(merged.map((e) => e.id)).toEqual(['e1']);
  });

  it('orders by the message each follows, then by when it happened', () => {
    const merged = mergeEvents(
      [event('late', 'TakenOver', 20)],
      [
        event('second', 'ReturnedToAi', 10, '2026-10-18T10:05:00Z'),
        event('first', 'TakenOver', 10, '2026-10-18T10:01:00Z'),
      ],
    );

    expect(merged.map((e) => e.id)).toEqual(['first', 'second', 'late']);
  });

  it('hands back the same array when nothing came, so nothing re-renders', () => {
    const current = [event('e1', 'TakenOver', 10)];

    expect(mergeEvents(current, [])).toBe(current);
    expect(mergeEvents(current, null)).toBe(current);
    expect(mergeEvents(current, undefined)).toBe(current);
  });
});

describe('refreshCursor', () => {
  const messages = (count: number): TranscriptMessage[] =>
    Array.from({ length: count }, (_, index) => ({
      id: `id-${index}`,
      sender: 'Customer',
      text: '',
      sentAt: '2026-10-18T10:00:00Z',
      sequence: (index + 1) * 10,
      sentBy: null,
    }));

  it('reads from the start while the transcript is shorter than the overlap', () => {
    expect(refreshCursor(messages(REFRESH_OVERLAP))).toBeNull();
  });

  it('re-reads a short overlap before the end, for a message that committed late', () => {
    // 30 messages, sequences 10..300: the re-read continues after the 21st from
    // the end, so the last 20 are read again and merged.
    expect(refreshCursor(messages(30))).toBe(100);
  });
});
