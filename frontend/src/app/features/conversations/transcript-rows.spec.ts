import type { TranscriptMessage } from './conversations-api';
import { REFRESH_OVERLAP, mergeMessages, refreshCursor, transcriptRows } from './transcript-rows';

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
      row.kind === 'message' ? row.key : row.kind === 'day' ? `day:${row.label}` : `to:${row.to}`,
    );

  it('puts a day separator before the first message of each day, and names today and yesterday', () => {
    const rows = transcriptRows(
      [
        message('a', 'Customer', at(16), 1),
        message('b', 'Customer', at(17), 2),
        message('c', 'Customer', at(17, 11), 3),
        message('d', 'Customer', at(18), 4),
      ],
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

  it('draws a handoff divider only where the replying voice changes', () => {
    // Customer messages between two AI replies do not split the AI's run, and
    // the first reply has nothing to hand off from.
    const rows = transcriptRows(
      [
        message('c1', 'Customer', at(18), 1),
        message('ai1', 'AiAgent', at(18), 2),
        message('c2', 'Customer', at(18), 3),
        message('ai2', 'AiAgent', at(18), 4),
        message('p1', 'HumanAgent', at(18), 5),
        message('p2', 'HumanAgent', at(18), 6),
        message('ai3', 'AiAgent', at(18), 7),
      ],
      'en',
      now,
    );

    expect(kinds(rows)).toEqual([
      'day:Today',
      'c1',
      'ai1',
      'c2',
      'ai2',
      'to:human',
      'p1',
      'p2',
      'to:ai',
      'ai3',
    ]);
  });

  it('invents no handoff for a conversation a person took over before anybody replied', () => {
    const rows = transcriptRows(
      [message('c1', 'Customer', at(18), 1), message('p1', 'HumanAgent', at(18), 2)],
      'en',
      now,
    );

    expect(rows.some((row) => row.kind === 'handoff')).toBe(false);
  });

  it('keeps the order it was given, whatever the timestamps say', () => {
    const rows = transcriptRows(
      [message('later', 'Customer', at(18, 11), 1), message('earlier', 'Customer', at(18, 9), 2)],
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
