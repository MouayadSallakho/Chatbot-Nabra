import type { ConversationSummary } from '@app/features/conversations/conversations-api';
import {
  BURST_CAP_MS,
  BURST_QUIET_MS,
  MAX_FAILURE_CHECKS,
  READ_GAP_MS,
  ReadPacer,
  RowMark,
  SignalInput,
  decideSignals,
} from './inbox-signal-rules';

const row = (id: string, over: Partial<ConversationSummary> = {}): ConversationSummary => ({
  id,
  channel: 'WebChat',
  status: 'Open',
  handling: 'Human',
  startedAt: '2026-11-08T10:00:00Z',
  lastActivityAt: '2026-11-08T10:05:00Z',
  agentName: null,
  messageCount: 2,
  lastMessagePreview: 'x',
  lastMessageSender: 'Customer',
  assignedTo: null,
  customer: null,
  unread: true,
  takenOverBy: null,
  needsPerson: true,
  needsPersonReason: 'PersonHandling',
  ...over,
});

/** What the last read remembered: an older time, and one message fewer than `row` has. */
const mark = (at: string, count = 1): RowMark => ({ at, count });

describe('decideSignals', () => {
  const OLD = '2026-11-08T10:00:00Z';
  const me = { userId: 'me', email: 'me@example.test' };

  const input = (over: Partial<SignalInput>): SignalInput => ({
    previous: new Map(),
    rows: [],
    me: 'me',
    viewing: new Set(),
    own: new Set(),
    openHere: null,
    ...over,
  });

  it('says a conversation entered when its id was not in the set', () => {
    const decision = decideSignals(
      input({ previous: new Map([['a', mark(OLD)]]), rows: [row('b')] }),
    );

    expect(decision.entered?.id).toBe('b');
  });

  it('names the most recent of several that entered', () => {
    const decision = decideSignals(input({ rows: [row('first'), row('second')] }));

    expect(decision.entered?.id).toBe('first');
  });

  it('says nothing when the set has not changed', () => {
    const rows = [row('a')];
    const previous = new Map([['a', mark(rows[0].lastActivityAt, rows[0].messageCount)]]);

    expect(decideSignals(input({ previous, rows }))).toEqual({
      entered: null,
      newMessage: null,
      failureChecks: [],
    });
  });

  describe('a new message', () => {
    const previous = new Map([['a', mark(OLD)]]);

    it('is told for my conversation when the customer wrote and it has moved', () => {
      const rows = [row('a', { assignedTo: me })];

      expect(decideSignals(input({ previous, rows })).newMessage?.id).toBe('a');
    });

    it('is not told for a conversation assigned to somebody else, or to nobody', () => {
      const other = { userId: 'other', email: 'o@example.test' };

      expect(
        decideSignals(input({ previous, rows: [row('a', { assignedTo: other })] })).newMessage,
      ).toBeNull();
      expect(decideSignals(input({ previous, rows: [row('a')] })).newMessage).toBeNull();
    });

    it('is not told while it is open in a visible tab', () => {
      const rows = [row('a', { assignedTo: me })];

      expect(
        decideSignals(input({ previous, rows, viewing: new Set(['a']) })).newMessage,
      ).toBeNull();
    });

    it('is not told when the customer did not write last', () => {
      const rows = [row('a', { assignedTo: me, lastMessageSender: 'HumanAgent' })];

      expect(decideSignals(input({ previous, rows })).newMessage).toBeNull();
    });

    it('is not told when it has no new message', () => {
      // The same count: nothing was written, whatever moved.
      const rows = [row('a', { assignedTo: me, lastActivityAt: OLD, messageCount: 1 })];

      expect(decideSignals(input({ previous, rows })).newMessage).toBeNull();
    });

    it('is told for a conversation this member took over (BA-2)', () => {
      const rows = [row('a', { takenOverBy: me })];

      expect(decideSignals(input({ previous, rows })).newMessage?.id).toBe('a');
    });

    it('is not told for a conversation somebody else took over', () => {
      const other = { userId: 'other', email: 'o@example.test' };

      expect(
        decideSignals(input({ previous, rows: [row('a', { takenOverBy: other })] })).newMessage,
      ).toBeNull();
    });

    it('is told once for a message, not again when the turn settles', () => {
      // Stored: the message arrives (count 2) and the row is read.
      const stored = row('a', { assignedTo: me, handling: 'Ai' });
      const first = decideSignals(input({ previous, rows: [stored] }));

      // Settled with a failure: the row moves again, and nothing new was written.
      const settled = row('a', {
        assignedTo: me,
        handling: 'Ai',
        lastActivityAt: '2026-11-08T10:05:11Z',
      });
      const second = decideSignals(
        input({ previous: new Map([['a', mark(stored.lastActivityAt, 2)]]), rows: [settled] }),
      );

      expect(first.newMessage?.id).toBe('a');
      expect(second.newMessage).toBeNull();
    });

    it('is not told when only the handling changed (an assignment moves the row too)', () => {
      const rows = [row('a', { assignedTo: me, messageCount: 1 })];

      expect(decideSignals(input({ previous, rows })).newMessage).toBeNull();
    });

    it('is not told when the member is not known', () => {
      const rows = [row('a', { assignedTo: me })];

      expect(decideSignals(input({ previous, rows, me: null })).newMessage).toBeNull();
    });
  });

  describe('which conversations need a look for a failed turn', () => {
    it('takes AI-handled ones that are new or moved, not those a person handles', () => {
      const rows = [
        row('new-ai', { handling: 'Ai' }),
        row('moved-ai', { handling: 'Ai' }),
        row('still-ai', { handling: 'Ai', lastActivityAt: OLD }),
        row('person'),
      ];
      const previous = new Map([
        ['moved-ai', mark(OLD)],
        ['still-ai', mark(OLD)],
      ]);

      expect(decideSignals(input({ previous, rows })).failureChecks.map((r) => r.id)).toEqual([
        'new-ai',
        'moved-ai',
      ]);
    });

    it('never asks about more than three', () => {
      const rows = ['a', 'b', 'c', 'd', 'e'].map((id) => row(id, { handling: 'Ai' }));

      expect(decideSignals(input({ rows })).failureChecks).toHaveLength(MAX_FAILURE_CHECKS);
    });

    it('leaves out the one open in this tab: its own read has the events', () => {
      const rows = [row('open', { handling: 'Ai' }), row('other', { handling: 'Ai' })];

      expect(
        decideSignals(input({ rows, openHere: 'open' })).failureChecks.map((r) => r.id),
      ).toEqual(['other']);
    });
  });

  it('is silent about a conversation this browser just acted on', () => {
    const rows = [row('a', { handling: 'Ai', assignedTo: me })];

    expect(decideSignals(input({ rows, own: new Set(['a']) }))).toEqual({
      entered: null,
      newMessage: null,
      failureChecks: [],
    });
  });
});

describe('ReadPacer', () => {
  let run: ReturnType<typeof vi.fn>;
  let pacer: ReadPacer;
  let finish: (() => void) | null;

  beforeEach(() => {
    vi.useFakeTimers();
    finish = null;
    run = vi.fn(() => Promise.resolve());
    pacer = new ReadPacer(run as unknown as () => Promise<void>);
  });

  afterEach(() => {
    pacer.stop();
    vi.useRealTimers();
  });

  const hanging = (): void => {
    run.mockImplementationOnce(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
  };

  it('asks for one read per burst, when the burst goes quiet', async () => {
    pacer.event();
    await vi.advanceTimersByTimeAsync(300);
    pacer.event();
    await vi.advanceTimersByTimeAsync(300);
    pacer.event();

    expect(run).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);

    expect(run).toHaveBeenCalledTimes(1);
  });

  it('closes a burst at the cap, however busy it is', async () => {
    for (let elapsed = 0; elapsed < BURST_CAP_MS; elapsed += 500) {
      pacer.event();
      await vi.advanceTimersByTimeAsync(500);
    }

    expect(run).toHaveBeenCalledTimes(1);
  });

  it('never starts two reads within five seconds', async () => {
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);

    expect(run).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(READ_GAP_MS);

    expect(run).toHaveBeenCalledTimes(2);
  });

  it('covers what arrives while a read waits with that one read', async () => {
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);

    for (let turn = 0; turn < 5; turn++) {
      pacer.event();
      await vi.advanceTimersByTimeAsync(100);
    }

    await vi.advanceTimersByTimeAsync(READ_GAP_MS * 3);

    expect(run).toHaveBeenCalledTimes(2);
  });

  it('reads exactly once more after events that arrived during a read', async () => {
    hanging();
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);

    pacer.event();
    pacer.event();
    pacer.event();
    finish!();
    await vi.advanceTimersByTimeAsync(READ_GAP_MS * 3);

    expect(run).toHaveBeenCalledTimes(2);
  });

  it('never exceeds twelve reads a minute, whatever the event rate', async () => {
    for (let elapsed = 0; elapsed < 60_000; elapsed += 100) {
      pacer.event();
      await vi.advanceTimersByTimeAsync(100);
    }

    expect(run.mock.calls.length).toBeLessThanOrEqual(12);
    expect(run.mock.calls.length).toBeGreaterThanOrEqual(10);
  });

  it('respects a wait the server asked for', async () => {
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS);
    pacer.hold(60_000);
    pacer.event();
    await vi.advanceTimersByTimeAsync(BURST_QUIET_MS + READ_GAP_MS);

    expect(run).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(60_000);

    expect(run).toHaveBeenCalledTimes(2);
  });

  it('reads straight away when asked to, with the limit still applying', async () => {
    pacer.soon();
    await vi.advanceTimersByTimeAsync(0);

    expect(run).toHaveBeenCalledTimes(1);

    pacer.soon();
    await vi.advanceTimersByTimeAsync(READ_GAP_MS - 1);

    expect(run).toHaveBeenCalledTimes(1);
  });

  it('does nothing once stopped', async () => {
    pacer.event();
    pacer.stop();
    await vi.advanceTimersByTimeAsync(READ_GAP_MS * 2);

    expect(run).not.toHaveBeenCalled();
  });
});
