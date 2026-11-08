import { SoundRequest } from './sound-names';

/** At most one sound in this long (section F, rule 5). */
export const QUIET_WINDOW_MS = 2_000;

/** A request, or the test button: the person's own click, which outranks everything. */
export interface SoundCandidate extends SoundRequest {
  readonly test?: true;
  readonly settled?: (heard: boolean) => void;
}

const SOUND_RANK = { 'new-message': 1, 'needs-person': 2, 'ai-failed': 3 } as const;
const TEST_RANK = 4;

function rank(candidate: SoundCandidate): number {
  return candidate.test ? TEST_RANK : SOUND_RANK[candidate.sound];
}

/**
 * Decides which of the sounds asked for is played, and when.
 *
 * <strong>One every two seconds, the most important one.</strong> Requests made
 * together are coalesced and the best plays. Inside the quiet window after a sound,
 * a better candidate waits for the window to end; one no better than what has just
 * played is dropped, because the screen (the count, the dots, the dividers) already
 * shows it.
 */
export class SoundScheduler {
  private pending: SoundCandidate | null = null;
  private playedAt = Number.NEGATIVE_INFINITY;
  private playedRank = 0;

  constructor(
    private readonly fire: (candidate: SoundCandidate) => void,
    private readonly now: () => number = Date.now,
  ) {}

  /** True when the candidate will play, unless a better one takes its place. */
  request(candidate: SoundCandidate): boolean {
    if (this.pending !== null) {
      if (rank(candidate) <= rank(this.pending)) {
        return false;
      }

      this.pending = candidate;

      return true;
    }

    const wait = this.playedAt + QUIET_WINDOW_MS - this.now();

    if (wait > 0 && rank(candidate) <= this.playedRank) {
      return false;
    }

    this.pending = candidate;
    // Never immediately, even with no wait: this gathers everything asked for in
    // the same moment, so the best of them is the one that plays.
    setTimeout(() => this.flush(), Math.max(0, wait));

    return true;
  }

  private flush(): void {
    const candidate = this.pending;

    this.pending = null;

    if (candidate !== null) {
      this.playedAt = this.now();
      this.playedRank = rank(candidate);
      this.fire(candidate);
    }
  }
}
