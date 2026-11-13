import type { ConversationSummary } from '@app/features/conversations/conversations-api';

/** A burst ends after this long with no further event. */
export const BURST_QUIET_MS = 750;

/** However busy it is, a burst ends after this long. */
export const BURST_CAP_MS = 2_000;

/** At most one needs-a-person read every this long: twelve a minute. */
export const READ_GAP_MS = 5_000;

/** The small transcript reads one needs-a-person read may cause. */
export const MAX_FAILURE_CHECKS = 3;

/** What one read of the needs-a-person set says should be told. */
export interface SignalDecision {
  /** The newest conversation that was not in the set before. */
  readonly entered: ConversationSummary | null;

  /** The newest conversation of this member's with a new customer message. */
  readonly newMessage: ConversationSummary | null;

  /** AI-handled conversations that changed: only a transcript read can say if a turn failed. */
  readonly failureChecks: readonly ConversationSummary[];
}

/** What one read remembers of a row: when it last moved, and how many messages it had. */
export interface RowMark {
  readonly at: string;
  readonly count: number;
}

/** The mark a row leaves for the next read to compare with. */
export function markOf(row: ConversationSummary): RowMark {
  return { at: row.lastActivityAt, count: row.messageCount };
}

export interface SignalInput {
  /** The set as last read: id → its mark. */
  readonly previous: ReadonlyMap<string, RowMark>;

  /** The set as just read, most recently active first. */
  readonly rows: readonly ConversationSummary[];

  /** The signed-in member, or null when that is not known yet. */
  readonly me: string | null;

  /** Conversations open in a visible tab of this browser. */
  readonly viewing: ReadonlySet<string>;

  /** Conversations this browser just acted on: its own actions are silent. */
  readonly own: ReadonlySet<string>;

  /** The conversation open in the reading tab, whose own transcript read supplies its events. */
  readonly openHere: string | null;
}

/**
 * Compares one read of the needs-a-person set with the one before.
 *
 * The hub sends only an id, so every sound is decided from authorised reads.
 *
 * <strong>The new-message sound is for this member's conversations:</strong> the
 * ones assigned to them, and since Phase 38.8 the ones they took over
 * (`takenOverBy`, BA-2; owner decision 7d kept them silent only until then).
 * Taking over does not assign, so both are read.
 *
 * <strong>One sound per message, not per announcement.</strong> Since 38.7a an AI
 * turn is announced twice (stored, then settled), and an assignment, a takeover or
 * a return also moves `lastActivityAt`. The sound therefore needs the row's
 * message count to have grown since the last read, with the customer writing last.
 */
export function decideSignals(input: SignalInput): SignalDecision {
  let entered: ConversationSummary | null = null;
  let newMessage: ConversationSummary | null = null;
  const failureChecks: ConversationSummary[] = [];

  for (const row of input.rows) {
    if (input.own.has(row.id)) {
      continue;
    }

    const before = input.previous.get(row.id);
    const isNew = before === undefined;
    const moved = !isNew && before.at !== row.lastActivityAt;
    const grew = !isNew && row.messageCount > before.count;

    if (isNew) {
      entered ??= row;
    }

    if (
      grew &&
      isMine(row, input.me) &&
      row.lastMessageSender === 'Customer' &&
      !input.viewing.has(row.id)
    ) {
      newMessage ??= row;
    }

    if (
      row.handling === 'Ai' &&
      (isNew || moved) &&
      row.id !== input.openHere &&
      failureChecks.length < MAX_FAILURE_CHECKS
    ) {
      failureChecks.push(row);
    }
  }

  return { entered, newMessage, failureChecks };
}

/** Assigned to this member, or taken over by them. Nobody's when the member is not known. */
function isMine(row: ConversationSummary, me: string | null): boolean {
  return me !== null && (row.assignedTo?.userId === me || row.takenOverBy?.userId === me);
}

/**
 * Turns any number of events into at most one read every five seconds.
 *
 * Events are gathered into a burst. A read starts when its burst closes, unless the
 * last read started under five seconds ago; then it waits for the five-second mark,
 * and whatever arrives meanwhile is covered by it. An event arriving while a read is
 * running causes exactly one more, so the last change is never missed.
 */
export class ReadPacer {
  private burstStartedAt = 0;
  private burstTimer: ReturnType<typeof setTimeout> | null = null;
  private waitTimer: ReturnType<typeof setTimeout> | null = null;
  private running = false;
  private again = false;
  private lastStartedAt = Number.NEGATIVE_INFINITY;
  private notBefore = Number.NEGATIVE_INFINITY;
  private stopped = false;

  constructor(private readonly run: () => Promise<void>) {}

  /** Something changed. */
  event(): void {
    if (this.stopped) {
      return;
    }

    if (this.running) {
      this.again = true;

      return;
    }

    if (this.waitTimer !== null) {
      return;
    }

    const now = Date.now();

    if (this.burstTimer === null) {
      this.burstStartedAt = now;
    } else {
      clearTimeout(this.burstTimer);
    }

    const left = this.burstStartedAt + BURST_CAP_MS - now;

    this.burstTimer = setTimeout(
      () => {
        this.burstTimer = null;
        this.schedule();
      },
      Math.max(0, Math.min(BURST_QUIET_MS, left)),
    );
  }

  /** Reads as soon as the limit allows, without waiting for a burst. */
  soon(): void {
    if (this.stopped) {
      return;
    }

    if (this.running) {
      this.again = true;
    } else if (this.waitTimer === null && this.burstTimer === null) {
      this.schedule();
    }
  }

  /** The server said to wait (`Retry-After`). */
  hold(milliseconds: number): void {
    this.notBefore = Date.now() + milliseconds;
  }

  stop(): void {
    this.stopped = true;

    if (this.burstTimer !== null) {
      clearTimeout(this.burstTimer);
    }

    if (this.waitTimer !== null) {
      clearTimeout(this.waitTimer);
    }
  }

  private schedule(): void {
    const wait = Math.max(this.lastStartedAt + READ_GAP_MS, this.notBefore) - Date.now();

    if (wait > 0) {
      this.waitTimer = setTimeout(() => {
        this.waitTimer = null;
        void this.fire();
      }, wait);
    } else {
      void this.fire();
    }
  }

  private async fire(): Promise<void> {
    if (this.stopped) {
      return;
    }

    this.running = true;
    this.again = false;
    this.lastStartedAt = Date.now();

    try {
      await this.run();
    } finally {
      this.running = false;

      if (this.again && !this.stopped) {
        this.again = false;
        this.schedule();
      }
    }
  }
}
