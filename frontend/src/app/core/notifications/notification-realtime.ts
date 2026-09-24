import { DestroyRef, Injectable, NgZone, inject, signal } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';

/** The one notification event the platform pushes. Its payload is a workspace id. */
const NOTIFICATIONS_CHANGED = 'notificationsChanged';

/** The same authenticated workspace hub the inbox uses. */
const WORKSPACE_HUB = '/hubs/workspace-inbox';

/**
 * Live notice that the signed-in member's notifications changed.
 *
 * <strong>Structural only.</strong> What arrives is "something changed in this
 * workspace" and nothing else — no title, no body, no count. The listener
 * re-reads through the authorised HTTP API, which stays the only source of
 * what a notification says.
 *
 * <strong>The same hub, its own subscription.</strong> It connects to the
 * Phase 19 workspace hub with the session the rest of the application uses, and
 * asks to join the member's own group. Membership is the only requirement, so a
 * member who may not read conversations still hears about their notifications.
 *
 * <strong>Losing the connection is not an error.</strong> SignalR reconnects on
 * its own; until it does, the bell is a few seconds stale and still correct,
 * and the next page load or panel open reads the truth.
 *
 * <strong>Loaded when needed.</strong> The bell is in every page's shell, so the
 * SignalR client is imported on first use rather than statically — it stays out
 * of the initial bundle and arrives after the page has rendered.
 */
@Injectable({ providedIn: 'root' })
export class NotificationRealtime {
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);

  private connection: HubConnection | null = null;
  private tenantId: string | null = null;
  private listener: (() => void) | null = null;

  /** Whether updates are arriving. False means stale-but-correct, never broken. */
  readonly live = signal(false);

  constructor() {
    this.destroyRef.onDestroy(() => void this.stop());
  }

  /** Listens to one workspace. Calling it for another workspace moves the subscription. */
  listen(tenantId: string, onChanged: () => void): void {
    this.listener = onChanged;

    if (this.tenantId !== tenantId) {
      void this.restart(tenantId);
    }
  }

  async stop(): Promise<void> {
    const connection = this.connection;

    this.connection = null;
    this.tenantId = null;
    this.live.set(false);

    if (connection && String(connection.state) !== 'Disconnected') {
      try {
        await connection.stop();
      } catch {
        // Stopping a socket that is already gone is not worth reporting.
      }
    }
  }

  private async restart(tenantId: string): Promise<void> {
    await this.stop();

    this.tenantId = tenantId;

    let connection: HubConnection;

    try {
      const signalr = await import('@microsoft/signalr');

      if (this.tenantId !== tenantId) {
        // The workspace changed while the client was loading.
        return;
      }

      connection = new signalr.HubConnectionBuilder()
        .withUrl(WORKSPACE_HUB)
        .withAutomaticReconnect()
        .configureLogging(signalr.LogLevel.Warning)
        .build();
    } catch {
      // No document to resolve the hub against (a test runner, a server render):
      // no live updates, and nothing else changes.
      return;
    }

    connection.on(NOTIFICATIONS_CHANGED, () => {
      this.zone.run(() => this.listener?.());
    });

    connection.onreconnected(() => {
      void this.subscribe(connection, tenantId);
      // Anything that happened while disconnected is fetched now.
      this.zone.run(() => this.listener?.());
    });

    connection.onreconnecting(() => this.zone.run(() => this.live.set(false)));
    connection.onclose(() => this.zone.run(() => this.live.set(false)));

    this.connection = connection;

    try {
      await this.zone.runOutsideAngular(() => connection.start());
      await this.subscribe(connection, tenantId);
    } catch {
      this.zone.run(() => this.live.set(false));
    }
  }

  private async subscribe(connection: HubConnection, tenantId: string): Promise<void> {
    try {
      await connection.invoke('SubscribeNotifications', tenantId);
      this.zone.run(() => this.live.set(true));
    } catch {
      this.zone.run(() => this.live.set(false));
    }
  }
}
