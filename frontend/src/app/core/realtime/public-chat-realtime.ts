import { DestroyRef, Injectable, NgZone, inject, signal } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { Observable } from 'rxjs';

/** The one event the platform pushes. */
const CONVERSATION_CHANGED = 'conversationChanged';

/** Where the visitor hub lives. */
const PUBLIC_HUB = '/hubs/public-chat';

/**
 * Mints a fresh realtime ticket through whichever transport the caller is on.
 *
 * Supplied by the caller rather than chosen here, because the two transports
 * authorise completely differently — a hosted page has a cookie it cannot read,
 * an embedded widget has a credential in a header — and this service is
 * deliberately ignorant of both.
 */
export type MintRealtimeTicket = () => Observable<{ readonly ticket: string }>;

/**
 * Live notice that the visitor's own conversation has changed.
 *
 * <strong>The conversation credential never reaches this file.</strong> A
 * websocket cannot carry a header, so whatever authorises the connection ends up
 * in the URL — logged by proxies, kept in access logs, visible in the network
 * panel of the page that opened it. So the caller mints a ticket through its own
 * already-authorised transport, and that is what travels: a value good for about
 * a minute, for one conversation, that reads nothing.
 *
 * <strong>A fresh ticket per connection attempt.</strong> The factory runs on
 * every connect and reconnect, which is what makes a ninety-second lifetime
 * workable — a socket that drops and returns ten minutes later mints a new one
 * rather than failing with a stale credential.
 *
 * <strong>The event carries no text.</strong> The widget re-reads its transcript
 * through the ordinary route, which re-checks the credential, the session's
 * expiry and whether the website is still approved. A widget on a site that was
 * removed from the allowlist a second ago can still be told something changed,
 * and still cannot read what.
 *
 * <strong>The realtime client loads on first use</strong> (Phase 36.9). Nobody
 * listens before a conversation exists, so a visitor who opens the page or the
 * widget and never sends anything never downloads it.
 */
@Injectable({ providedIn: 'root' })
export class PublicChatRealtime {
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);

  private connection: HubConnection | null = null;

  /** Bumped by every listen and stop, so a slow load cannot revive a stopped listener. */
  private generation = 0;

  /** Whether updates are arriving. False means the visitor may need to reload. */
  readonly live = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => void this.stop());
  }

  /**
   * Starts listening for changes to this visitor's conversation.
   *
   * @param mintTicket How to obtain a ticket. Called once per connection attempt.
   * @param onChanged Called when the conversation changes. Takes no payload
   *   worth reading: the caller re-reads its transcript.
   */
  async listen(mintTicket: MintRealtimeTicket, onChanged: () => void): Promise<void> {
    await this.stop();

    const generation = this.generation;

    // Built inside the try for the same reason the admin one is: `withUrl`
    // resolves against the document and throws where there is none, and a
    // widget must not be taken down by the absence of a feature it can live
    // without.
    let connection: HubConnection;

    try {
      const signalr = await import('@microsoft/signalr');

      if (generation !== this.generation) {
        // Stopped, or asked to listen elsewhere, while the client was loading.
        return;
      }

      connection = new signalr.HubConnectionBuilder()
        .withUrl(PUBLIC_HUB, {
          // Called again on every reconnect, so the ticket presented is always
          // a current one rather than the one this page started with.
          accessTokenFactory: async () => {
            const minted = await firstValue(mintTicket());

            return minted.ticket;
          },
        })
        .withAutomaticReconnect()
        .configureLogging(signalr.LogLevel.Warning)
        .build();
    } catch {
      this.live.set(false);

      return;
    }

    connection.on(CONVERSATION_CHANGED, () => this.zone.run(() => onChanged()));

    connection.onreconnected(() => this.zone.run(() => this.live.set(true)));
    connection.onreconnecting(() => this.zone.run(() => this.live.set(false)));
    connection.onclose(() => this.zone.run(() => this.live.set(false)));

    this.connection = connection;

    try {
      await this.zone.runOutsideAngular(() => connection.start());
      this.zone.run(() => this.live.set(true));
    } catch {
      // No live updates on this deployment, or the ticket route refused. The
      // chat itself is unaffected — sending and reading both still work — so
      // the visitor is told nothing. A chat that announced an infrastructure
      // problem it has already worked around would only be alarming.
      this.zone.run(() => this.live.set(false));
    }
  }

  async stop(): Promise<void> {
    const connection = this.connection;

    this.generation += 1;
    this.connection = null;
    this.live.set(false);

    if (connection && String(connection.state) !== 'Disconnected') {
      try {
        await connection.stop();
      } catch {
        // Stopping a socket that is already gone is not a failure.
      }
    }
  }
}

/** The first value of a one-shot observable, as a promise. */
function firstValue<T>(source: Observable<T>): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const subscription = source.subscribe({
      next: (value) => {
        resolve(value);
        subscription.unsubscribe();
      },
      error: reject,
      complete: () => reject(new Error('The realtime ticket request produced no value.')),
    });
  });
}
