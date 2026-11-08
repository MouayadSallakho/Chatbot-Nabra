import {
  EnvironmentInjector,
  Injectable,
  InjectionToken,
  NgZone,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import type { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { ADMIN_RECONNECT_POLICY } from './reconnect-policy';

/** Where the workspace hub lives. */
const WORKSPACE_HUB = '/hubs/workspace-inbox';

/**
 * Where the hub connection's builder comes from: SignalR's own, in the product.
 *
 * Asynchronous because the shell is in the startup bundle and SignalR must not
 * be. A token rather than a direct import so a test supplies a stand-in through
 * TestBed: mocking the `@microsoft/signalr` module was order-dependent, because
 * the unit-test builder runs specs without isolation (Phase 36.7, revision 1).
 */
export const HUB_CONNECTION_BUILDER = new InjectionToken<() => Promise<HubConnectionBuilder>>(
  'HUB_CONNECTION_BUILDER',
  {
    providedIn: 'root',
    factory: () => async () => {
      const signalr = await import('@microsoft/signalr');

      return new signalr.HubConnectionBuilder().configureLogging(signalr.LogLevel.Warning);
    },
  },
);

/**
 * What a listener is told: the conversation that changed, or `null` when the
 * connection has just come back and anything may have changed while it was down.
 */
export type ConversationChanged = (conversationId: string | null) => void;

/** One open connection, for one workspace. */
interface Session {
  readonly tenantId: string;
  connection: HubConnection | null;
  connected: boolean;
  conversationsJoined: boolean;

  /** Group changes run one at a time, so the last wish is the one that lands. */
  queue: Promise<void>;
}

/**
 * The page's one connection to the workspace hub, shared by the bell and the inbox.
 *
 * <strong>It carries identifiers and nothing else, by design.</strong> The server
 * sends a conversation id, or says the member's notifications changed; whoever
 * listens re-reads through the ordinary authorised API. No transcript text travels
 * here, so a connection authorised a while ago cannot become a way to read
 * something its holder may no longer see.
 *
 * <strong>Arrival order is not transcript order.</strong> Two API instances behind
 * a backplane deliver when they deliver. What the transcript is comes from the
 * ingestion sequence on the read that follows.
 *
 * <strong>The server decides who joins.</strong> The member's own notifications
 * need only membership, so that group is always asked for. The conversation group
 * needs `conversations.view`: it is asked for only while the member holds that
 * permission, and left when they lose it.
 *
 * <strong>It follows the workspace.</strong> The shell starts it once; from then on
 * a chosen workspace opens the connection, another one replaces it, and signing out
 * (which clears the workspace) closes it.
 *
 * <strong>Losing the connection is not an error worth showing.</strong> SignalR
 * reconnects on its own, and a screen whose socket has dropped is a few seconds
 * stale and still correct. Events sent while it was down are gone, so after a
 * reconnect every listener is told to re-read.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceRealtime {
  private readonly zone = inject(NgZone);
  private readonly injector = inject(EnvironmentInjector);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly createBuilder = inject(HUB_CONNECTION_BUILDER);

  private readonly conversationListeners = new Set<ConversationChanged>();
  private readonly notificationListeners = new Set<() => void>();

  private started = false;
  private session: Session | null = null;
  private wantConversations = false;

  /** The previous connection's goodbye: the next one waits for it, so two never overlap. */
  private closing: Promise<void> = Promise.resolve();

  private readonly hasConversations = signal(false);
  private readonly hasNotifications = signal(false);

  /** Whether conversation events are arriving. False means stale-but-correct. */
  readonly conversationsLive = this.hasConversations.asReadonly();

  /** Whether notification events are arriving. False means stale-but-correct. */
  readonly notificationsLive = this.hasNotifications.asReadonly();

  /** Connects to the current workspace from now on. Called by the shell; safe to repeat. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;

    effect(
      () => {
        const tenantId = this.workspace.tenantId();
        // Compared with the workspace because a switch keeps the previous
        // workspace's permissions until the new answer arrives.
        const conversations =
          tenantId !== null &&
          this.permissions.tenantId() === tenantId &&
          this.permissions.has('conversations.view');

        untracked(() => this.apply(tenantId, conversations));
      },
      { injector: this.injector },
    );
  }

  /** The workspace the open connection belongs to. */
  tenantId(): string | null {
    return this.session?.tenantId ?? null;
  }

  /** Calls back on every conversation event. Returns the unsubscribe. */
  onConversation(listener: ConversationChanged): () => void {
    this.conversationListeners.add(listener);

    return () => this.conversationListeners.delete(listener);
  }

  /** Calls back on every notification event. Returns the unsubscribe. */
  onNotifications(listener: () => void): () => void {
    this.notificationListeners.add(listener);

    return () => this.notificationListeners.delete(listener);
  }

  private apply(tenantId: string | null, conversations: boolean): void {
    this.wantConversations = conversations;

    if (this.session !== null && this.session.tenantId !== tenantId) {
      this.close(this.session);
    }

    if (tenantId === null) {
      return;
    }

    if (this.session === null) {
      void this.open(tenantId);
    } else {
      void this.reconcile(this.session);
    }
  }

  private async open(tenantId: string): Promise<void> {
    const session: Session = {
      tenantId,
      connection: null,
      connected: false,
      conversationsJoined: false,
      queue: Promise.resolve(),
    };

    // Claimed before anything is awaited, so a second wish in the same tick
    // sees this one rather than opening another socket.
    this.session = session;

    let connection: HubConnection;

    // Inside the try: `withUrl` throws where there is no document to resolve the
    // address against (a test runner, a server render), and a feature whose
    // absence changes nothing must not take down whatever is running.
    try {
      await this.closing;

      const builder = await this.createBuilder();

      if (this.session !== session) {
        return;
      }

      connection = builder
        .withUrl(WORKSPACE_HUB)
        .withAutomaticReconnect(ADMIN_RECONNECT_POLICY)
        .build();
    } catch {
      return;
    }

    session.connection = connection;
    this.listen(session, connection);

    try {
      // Outside Angular's zone on the way in, back inside for the listeners: a
      // socket that fires often must not schedule change detection on every frame.
      await this.zone.runOutsideAngular(() => connection.start());
    } catch {
      // A hub that will not start is a screen without live updates. Nothing is
      // shown: there is nothing the reader can do, and their data is correct.
      return;
    }

    if (this.session === session) {
      session.connected = true;
      await this.joinGroups(session);
    }
  }

  private listen(session: Session, connection: HubConnection): void {
    connection.on('conversationChanged', (conversationId: string) => {
      this.tell(this.conversationListeners, conversationId);
    });

    connection.on('notificationsChanged', () => {
      this.zone.run(() => this.callNotificationListeners());
    });

    connection.onreconnecting(() => {
      session.connected = false;
      session.conversationsJoined = false;
      this.publish(session, false);
    });

    connection.onreconnected(() => void this.rejoin(session));

    connection.onclose(() => {
      session.connected = false;
      session.conversationsJoined = false;
      this.publish(session, false);
    });
  }

  /** A reconnect is a new connection to the server: its groups have to be asked for again. */
  private async rejoin(session: Session): Promise<void> {
    session.connected = true;
    session.conversationsJoined = false;

    await this.joinGroups(session);

    if (this.session !== session) {
      return;
    }

    // After joining, not before: an event between the re-read and the join
    // would otherwise be lost as surely as the ones sent during the outage.
    this.tell(this.conversationListeners, null);
    this.zone.run(() => this.callNotificationListeners());
  }

  private async joinGroups(session: Session): Promise<void> {
    let notifications = false;

    try {
      await session.connection?.invoke('SubscribeNotifications', session.tenantId);
      notifications = true;
    } catch {
      // Refused or dropped: the bell is stale-but-correct until the next read.
    }

    this.publish(session, notifications);
    await this.reconcile(session);
  }

  /** Brings the conversation group in line with the member's permission. */
  private reconcile(session: Session): Promise<void> {
    session.queue = session.queue.then(async () => {
      const connection = session.connection;
      const want = this.wantConversations;

      if (
        this.session !== session ||
        !session.connected ||
        connection === null ||
        want === session.conversationsJoined
      ) {
        return;
      }

      try {
        await connection.invoke(want ? 'Subscribe' : 'Unsubscribe', session.tenantId);
        session.conversationsJoined = want;
      } catch {
        session.conversationsJoined = false;
      }

      this.zone.run(() => this.hasConversations.set(session.conversationsJoined));
    });

    return session.queue;
  }

  private close(session: Session): void {
    this.session = null;
    session.connected = false;
    session.conversationsJoined = false;
    this.hasConversations.set(false);
    this.hasNotifications.set(false);

    const connection = session.connection;

    if (connection !== null && String(connection.state) !== 'Disconnected') {
      // Stopping a socket that is already gone is not worth reporting.
      this.closing = connection.stop().catch(() => undefined);
    }
  }

  private publish(session: Session, notifications: boolean): void {
    if (this.session !== session) {
      return;
    }

    this.zone.run(() => {
      this.hasNotifications.set(notifications);
      this.hasConversations.set(session.conversationsJoined);
    });
  }

  private tell(listeners: Set<ConversationChanged>, conversationId: string | null): void {
    this.zone.run(() => {
      // A copy: a listener may unsubscribe while it is being told.
      for (const listener of [...listeners]) {
        listener(conversationId);
      }
    });
  }

  private callNotificationListeners(): void {
    for (const listener of [...this.notificationListeners]) {
      listener();
    }
  }
}
