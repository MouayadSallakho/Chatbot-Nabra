import { DestroyRef, Injectable, NgZone, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { ADMIN_RECONNECT_POLICY } from './reconnect-policy';

/** The one event the platform pushes. Its payload is a conversation id. */
const CONVERSATION_CHANGED = 'conversationChanged';

/** Where the workspace hub lives. */
const WORKSPACE_HUB = '/hubs/workspace-inbox';

/**
 * What a listener is told: the conversation that changed, or `null` when the
 * connection has just come back and anything may have changed while it was down.
 */
export type ConversationChanged = (conversationId: string | null) => void;

/**
 * Live notice that a conversation in this workspace has changed.
 *
 * <strong>It delivers an identifier and nothing else, by design.</strong> The
 * server sends a conversation id; this service hands it to whoever is listening;
 * the listener re-reads through the ordinary authorised API. No transcript text
 * travels on this channel, so a connection that was authorised a while ago
 * cannot become a way to read something the reader is no longer allowed to see.
 *
 * <strong>Arrival order is not transcript order.</strong> Two API instances
 * behind a backplane deliver when they deliver. What the transcript *is* comes
 * from the ingestion sequence on the read that follows, exactly as it does for
 * somebody who pressed Refresh.
 *
 * <strong>Losing the connection is not an error worth showing.</strong> SignalR
 * reconnects on its own; a screen whose socket has dropped is a screen that is a
 * few seconds stale and still correct, and its Refresh button still works. The
 * connection state is published so a view can say so quietly if it wants to, and
 * nothing here blocks or fails a render.
 */
@Injectable({ providedIn: 'root' })
export class ConversationRealtime {
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);

  private connection: HubConnection | null = null;
  private tenantId: string | null = null;
  private readonly listeners = new Set<ConversationChanged>();

  /** Whether updates are arriving. False means stale-but-correct, never broken. */
  readonly live = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => void this.stop());
  }

  /**
   * Starts listening to one workspace, and calls back on every change.
   *
   * Returns the unsubscribe. Repeated calls for the same workspace share one
   * socket: an inbox with a list and a detail open should not hold two.
   */
  listen(tenantId: string, onChanged: ConversationChanged): () => void {
    this.listeners.add(onChanged);

    if (this.tenantId !== tenantId) {
      // Claimed at once, before anything is awaited. The inbox subscribes its
      // list and its open conversation in the same tick (Phase 36.6); a second
      // call arriving while the first was still tearing down used to see no
      // workspace yet and open a second, then a third, socket.
      this.tenantId = tenantId;
      void this.restart(tenantId);
    }

    return () => {
      this.listeners.delete(onChanged);

      if (this.listeners.size === 0) {
        void this.stop();
      }
    };
  }

  private async restart(tenantId: string): Promise<void> {
    await this.disconnect();

    // Another workspace was asked for while this one was tearing down.
    if (this.tenantId !== tenantId) {
      return;
    }

    // Building the connection is inside the try, not before it. `withUrl`
    // resolves the address against the document, which throws outright where
    // there is no document to resolve against — a test runner, a server-side
    // render. An exception from a constructor two lines above a try block is an
    // unhandled rejection that takes down whatever is running, for a feature
    // whose whole contract is that its absence changes nothing.
    let connection: HubConnection;

    try {
      connection = new HubConnectionBuilder()
        .withUrl(WORKSPACE_HUB)
        .withAutomaticReconnect(ADMIN_RECONNECT_POLICY)
        .configureLogging(LogLevel.Warning)
        .build();
    } catch {
      this.live.set(false);

      return;
    }

    // Outside Angular's zone on the way in, back inside on the way out. SignalR
    // holds a socket that fires often; letting every frame schedule change
    // detection would make an idle inbox busy. Re-entering for the callback is
    // what makes the signals a listener writes to actually repaint.
    connection.on(CONVERSATION_CHANGED, (conversationId: string) => {
      this.zone.run(() => {
        for (const listener of this.listeners) {
          listener(conversationId);
        }
      });
    });

    connection.onreconnected(() => {
      void this.subscribe(connection, tenantId);
      this.zone.run(() => {
        this.live.set(true);

        // Pushes sent while the socket was down are gone. Every listener is
        // told to re-read, through the ordinary authorised API.
        for (const listener of this.listeners) {
          listener(null);
        }
      });
    });

    connection.onreconnecting(() => this.zone.run(() => this.live.set(false)));
    connection.onclose(() => this.zone.run(() => this.live.set(false)));

    this.connection = connection;

    try {
      await this.zone.runOutsideAngular(() => connection.start());
      await this.subscribe(connection, tenantId);
      this.zone.run(() => this.live.set(true));
    } catch {
      // A hub that will not start is a screen without live updates, which is
      // the state this application was in until this release. Nothing is
      // reported to the reader: there is nothing they can do, and the data on
      // their screen is correct.
      this.zone.run(() => this.live.set(false));
    }
  }

  /**
   * Asks the server for this workspace's events.
   *
   * The server decides. A workspace the reader does not belong to, or may not
   * read conversations in, is refused there — this call merely asks.
   */
  private async subscribe(connection: HubConnection, tenantId: string): Promise<void> {
    try {
      await connection.invoke('Subscribe', tenantId);
    } catch {
      this.zone.run(() => this.live.set(false));
    }
  }

  /** Stops listening altogether: the last listener has gone. */
  private async stop(): Promise<void> {
    this.tenantId = null;
    await this.disconnect();
  }

  /** Closes the socket, if there is one, without forgetting which workspace is wanted. */
  private async disconnect(): Promise<void> {
    const connection = this.connection;

    this.connection = null;
    this.live.set(false);

    if (connection && connection.state !== HubConnectionState.Disconnected) {
      try {
        await connection.stop();
      } catch {
        // Stopping a socket that is already gone is not a failure worth
        // propagating out of a teardown path.
      }
    }
  }
}
