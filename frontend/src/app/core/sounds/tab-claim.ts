import { Injectable, inject } from '@angular/core';
import { SOUND_PLATFORM } from './sound-platform';

const CHANNEL_NAME = 'nabra:sounds';
const LOCK_NAME = 'nabra:sound-play';

/** A hidden tab gives a visible one this long to speak first. */
export const HIDDEN_TAB_DELAY_MS = 400;

/** How long a win is held: also the limit of one sound per two seconds across tabs. */
export const CLAIM_HOLD_MS = 2_000;

/** Without Web Locks, how long tabs listen for each other before the lowest id plays. */
export const ELECTION_MS = 60;

type TabMessage = { readonly kind: 'visible' } | { readonly kind: 'claim'; readonly id: string };

const wait = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Elects the one tab that plays (section F, rule 6).
 *
 * A visible tab tries at once. A hidden tab waits a moment and stands aside if a
 * visible one has spoken. The winner is whoever takes the Web Lock; where Web Locks
 * are missing, the tabs elect over the channel, and a browser with neither has only
 * this tab to consider.
 */
@Injectable({ providedIn: 'root' })
export class TabClaim {
  private readonly platform = inject(SOUND_PLATFORM);

  // Only ever compared with the other tabs' ids; never sent anywhere else.
  private readonly id = `${Date.now()}-${Math.random()}`;
  private readonly channel = this.openChannel();

  private visibleHeardAt = Number.NEGATIVE_INFINITY;
  private claimHeardAt = Number.NEGATIVE_INFINITY;
  private readonly rivals = new Set<string>();

  /** True when this tab is the one to play. */
  async claim(): Promise<boolean> {
    const started = Date.now();

    if (this.platform.isVisible()) {
      this.post({ kind: 'visible' });
    } else {
      await wait(HIDDEN_TAB_DELAY_MS);

      if (this.visibleHeardAt >= started - HIDDEN_TAB_DELAY_MS) {
        return false;
      }
    }

    return this.platform.locks ? this.takeLock(this.platform.locks) : this.elect();
  }

  private takeLock(locks: LockManager): Promise<boolean> {
    return new Promise((resolve) => {
      locks
        .request(LOCK_NAME, { ifAvailable: true }, (lock) => {
          resolve(lock !== null);

          return lock === null ? undefined : wait(CLAIM_HOLD_MS);
        })
        // A lock manager that refuses (an opaque origin) leaves this tab alone.
        .catch(() => resolve(true));
    });
  }

  private async elect(): Promise<boolean> {
    if (this.channel === null) {
      return true;
    }

    if (Date.now() - this.claimHeardAt < CLAIM_HOLD_MS) {
      return false;
    }

    this.rivals.clear();
    this.post({ kind: 'claim', id: this.id });
    await wait(ELECTION_MS);

    return [...this.rivals].every((rival) => this.id < rival);
  }

  private post(message: TabMessage): void {
    try {
      this.channel?.postMessage(message);
    } catch {
      // A channel that has gone away leaves this tab to decide alone.
    }
  }

  private openChannel(): BroadcastChannel | null {
    let channel: BroadcastChannel | null;

    try {
      channel = this.platform.createChannel(CHANNEL_NAME);
    } catch {
      return null;
    }

    if (channel !== null) {
      channel.onmessage = ({ data }: MessageEvent<TabMessage>) => {
        if (data?.kind === 'visible') {
          this.visibleHeardAt = Date.now();
        } else if (data?.kind === 'claim') {
          this.claimHeardAt = Date.now();
          this.rivals.add(data.id);
        }
      };
    }

    return channel;
  }
}
