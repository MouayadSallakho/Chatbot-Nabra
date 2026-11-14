import { TestBed } from '@angular/core/testing';
import type { HubConnectionBuilder } from '@microsoft/signalr';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { HUB_CONNECTION_BUILDER, WorkspaceRealtime } from './workspace-realtime';

const built: FakeConnection[] = [];

/** A stand-in for the SignalR client: the test is about who joins what, not the transport. */
class FakeConnection {
  state = 'Disconnected';
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  readonly invoked: unknown[][] = [];
  refuse = new Set<string>();
  reconnected: () => void = () => undefined;
  reconnecting: () => void = () => undefined;

  on(name: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(name, handler);
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  onreconnecting(handler: () => void): void {
    this.reconnecting = handler;
  }

  onclose(): void {}

  async start(): Promise<void> {
    this.state = 'Connected';
  }

  async stop(): Promise<void> {
    this.state = 'Disconnected';
  }

  async invoke(...args: unknown[]): Promise<void> {
    if (this.refuse.has(String(args[0]))) {
      throw new Error('No such workspace.');
    }

    this.invoked.push(args);
  }
}

class FakeBuilder {
  withUrl(): this {
    return this;
  }

  withAutomaticReconnect(): this {
    return this;
  }

  build(): FakeConnection {
    const connection = new FakeConnection();

    built.push(connection);

    return connection;
  }
}

describe('WorkspaceRealtime', () => {
  const tenant = { id: 't1', name: 'Acme', slug: 'acme', status: 'Active' as const };
  const other = { id: 't2', name: 'Other', slug: 'other', status: 'Active' as const };
  const VIEW = 'conversations.view';

  const settle = async (): Promise<void> => {
    for (let turn = 0; turn < 4; turn++) {
      TestBed.tick();
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
  };

  function setUp(...codes: string[]): {
    realtime: WorkspaceRealtime;
    workspace: WorkspaceContextService;
  } {
    built.length = 0;
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(...codes) },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        {
          provide: HUB_CONNECTION_BUILDER,
          useValue: async () => new FakeBuilder() as unknown as HubConnectionBuilder,
        },
      ],
    });

    return {
      realtime: TestBed.inject(WorkspaceRealtime),
      workspace: TestBed.inject(WorkspaceContextService),
    };
  }

  it('opens nothing until the shell starts it', async () => {
    const { workspace } = setUp(VIEW);

    await workspace.adopt(tenant);
    await settle();

    expect(built).toHaveLength(0);
  });

  it('opens one connection and joins both groups for somebody who can read conversations', async () => {
    const { realtime, workspace } = setUp(VIEW);

    realtime.start();
    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    expect(built).toHaveLength(1);
    expect(built[0].invoked).toEqual([
      ['SubscribeNotifications', 't1'],
      ['Subscribe', 't1'],
    ]);
    expect(realtime.notificationsLive()).toBe(true);
    expect(realtime.conversationsLive()).toBe(true);
  });

  it('joins only the notification group without conversations.view', async () => {
    const { realtime, workspace } = setUp();

    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    expect(built[0].invoked).toEqual([['SubscribeNotifications', 't1']]);
    expect(realtime.notificationsLive()).toBe(true);
    expect(realtime.conversationsLive()).toBe(false);
  });

  it('leaves the conversation group when the permission is lost, and rejoins when it returns', async () => {
    const { realtime, workspace } = setUp(VIEW);
    const permissions = TestBed.inject(PermissionService);

    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    await permissions.load('t1', Promise.resolve([]));
    await settle();

    expect(built[0].invoked.at(-1)).toEqual(['Unsubscribe', 't1']);
    expect(realtime.conversationsLive()).toBe(false);
    expect(built).toHaveLength(1);

    await permissions.load('t1', Promise.resolve([VIEW]));
    await settle();

    expect(built[0].invoked.at(-1)).toEqual(['Subscribe', 't1']);
    expect(realtime.conversationsLive()).toBe(true);
  });

  it('reads a refusal from the server as not live, and nothing more', async () => {
    const { realtime, workspace } = setUp(VIEW);

    realtime.start();
    await workspace.adopt(tenant);
    built[0]?.refuse.add('Subscribe');
    await settle();

    expect(realtime.notificationsLive()).toBe(true);
  });

  it('tells the bell and the inbox about their own events only', async () => {
    const { realtime, workspace } = setUp(VIEW);
    const conversations: (string | null)[] = [];
    const notifications = vi.fn();

    realtime.onConversation((id) => conversations.push(id));
    realtime.onNotifications(notifications);
    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    built[0].handlers.get('conversationChanged')!('c1');

    expect(conversations).toEqual(['c1']);
    expect(notifications).not.toHaveBeenCalled();

    built[0].handlers.get('notificationsChanged')!('t1');

    expect(conversations).toEqual(['c1']);
    expect(notifications).toHaveBeenCalledTimes(1);
  });

  it('tells the inbox when the member marked every conversation read, and nobody else (39.1)', async () => {
    const { realtime, workspace } = setUp(VIEW);
    const conversations: (string | null)[] = [];
    const read: (string | null)[] = [];
    const notifications = vi.fn();

    realtime.onConversation((id) => conversations.push(id));
    realtime.onNotifications(notifications);
    const stop = realtime.onConversationsRead((id) => read.push(id));
    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    built[0].handlers.get('conversationsRead')!(tenant.id);

    expect(read).toEqual([tenant.id]);
    expect(conversations).toEqual([]);
    expect(notifications).not.toHaveBeenCalled();

    stop();
    built[0].handlers.get('conversationsRead')!(tenant.id);

    expect(read).toEqual([tenant.id]);
  });

  it('survives a listener that unsubscribes while it is being told', async () => {
    const { realtime, workspace } = setUp(VIEW);
    const later = vi.fn();
    const stop = realtime.onConversation(() => stop());

    realtime.onConversation(later);
    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    built[0].handlers.get('conversationChanged')!('c1');

    expect(later).toHaveBeenCalledWith('c1');
  });

  it('after a reconnect joins both groups again, and only then tells everybody to re-read', async () => {
    const { realtime, workspace } = setUp(VIEW);
    const order: string[] = [];

    realtime.onConversation((id) => order.push(`inbox:${id}`));
    realtime.onNotifications(() => order.push('bell'));
    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    const connection = built[0];

    connection.reconnecting();
    expect(realtime.conversationsLive()).toBe(false);
    expect(realtime.notificationsLive()).toBe(false);

    connection.invoked.length = 0;
    connection.reconnected();

    // Nothing is said before the groups are joined.
    expect(order).toEqual([]);

    await settle();

    expect(connection.invoked).toEqual([
      ['SubscribeNotifications', 't1'],
      ['Subscribe', 't1'],
    ]);
    expect(order).toEqual(['inbox:null', 'bell']);
    expect(realtime.conversationsLive()).toBe(true);
    expect(built).toHaveLength(1);
  });

  it('moves to the next workspace on one connection at a time', async () => {
    const { realtime, workspace } = setUp(VIEW);

    realtime.start();
    await workspace.adopt(tenant);
    await settle();
    await workspace.adopt(other);
    await settle();

    expect(built).toHaveLength(2);
    expect(built[0].state).toBe('Disconnected');
    expect(built[1].state).toBe('Connected');
    expect(built[1].invoked).toContainEqual(['Subscribe', 't2']);
    expect(realtime.tenantId()).toBe('t2');
  });

  it('closes when the workspace is cleared, as signing out does', async () => {
    const { realtime, workspace } = setUp(VIEW);

    realtime.start();
    await workspace.adopt(tenant);
    await settle();

    workspace.clear();
    await settle();

    expect(built[0].state).toBe('Disconnected');
    expect(realtime.tenantId()).toBeNull();
    expect(realtime.conversationsLive()).toBe(false);
    expect(realtime.notificationsLive()).toBe(false);
  });

  describe('the knowledge group (Phase 38.8, BA-11)', () => {
    const KNOWLEDGE = 'knowledge.view';

    const knowledgeCalls = (connection: FakeConnection) =>
      connection.invoked.filter(([name]) => String(name).includes('Knowledge'));

    it('joins only while a knowledge screen listens, and leaves with the last one', async () => {
      const { realtime, workspace } = setUp(VIEW, KNOWLEDGE);

      realtime.start();
      await workspace.adopt(tenant);
      await settle();

      // On the inbox: no knowledge group.
      expect(knowledgeCalls(built[0])).toEqual([]);

      const leaveList = realtime.onKnowledge(() => undefined);
      const leaveEditor = realtime.onKnowledge(() => undefined);
      await settle();

      expect(knowledgeCalls(built[0])).toEqual([['SubscribeKnowledge', 't1']]);

      leaveList();
      await settle();

      expect(knowledgeCalls(built[0])).toEqual([['SubscribeKnowledge', 't1']]);

      leaveEditor();
      await settle();

      expect(knowledgeCalls(built[0])).toEqual([
        ['SubscribeKnowledge', 't1'],
        ['UnsubscribeKnowledge', 't1'],
      ]);
      // Still one connection: the knowledge group rides on it.
      expect(built).toHaveLength(1);
    });

    it('asks nothing for somebody without knowledge.view', async () => {
      const { realtime, workspace } = setUp(VIEW);

      realtime.start();
      await workspace.adopt(tenant);
      realtime.onKnowledge(() => undefined);
      await settle();

      expect(knowledgeCalls(built[0])).toEqual([]);
    });

    it('takes a refusal as no permission, and does not ask again', async () => {
      const { realtime, workspace } = setUp(VIEW, KNOWLEDGE);

      realtime.start();
      await workspace.adopt(tenant);
      await settle();
      built[0].refuse.add('SubscribeKnowledge');

      const leave = realtime.onKnowledge(() => undefined);
      await settle();
      leave();
      realtime.onKnowledge(() => undefined);
      await settle();

      // Refused calls are not recorded by the fake; nothing else was tried either.
      expect(knowledgeCalls(built[0])).toEqual([]);
      built[0].refuse.clear();
      realtime.onKnowledge(() => undefined);
      await settle();

      expect(knowledgeCalls(built[0])).toEqual([]);
    });

    it('tells knowledge listeners about knowledge events only', async () => {
      const { realtime, workspace } = setUp(VIEW, KNOWLEDGE);
      const documents: (string | null)[] = [];
      const conversations: (string | null)[] = [];

      realtime.onKnowledge((id) => documents.push(id));
      realtime.onConversation((id) => conversations.push(id));
      realtime.start();
      await workspace.adopt(tenant);
      await settle();

      built[0].handlers.get('knowledgeChanged')!('d1');
      built[0].handlers.get('conversationChanged')!('c1');

      expect(documents).toEqual(['d1']);
      expect(conversations).toEqual(['c1']);
    });

    it('after a reconnect joins the knowledge group again, then tells the screen to re-read', async () => {
      const { realtime, workspace } = setUp(VIEW, KNOWLEDGE);
      const documents: (string | null)[] = [];

      realtime.onKnowledge((id) => documents.push(id));
      realtime.start();
      await workspace.adopt(tenant);
      await settle();

      const connection = built[0];

      connection.reconnecting();
      connection.invoked.length = 0;
      connection.reconnected();

      expect(documents).toEqual([]);

      await settle();

      expect(knowledgeCalls(connection)).toEqual([['SubscribeKnowledge', 't1']]);
      expect(documents).toEqual([null]);
    });
  });

  it('changes nothing else when the client cannot be built', async () => {
    built.length = 0;
    TestBed.configureTestingModule({
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(VIEW) },
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        {
          provide: HUB_CONNECTION_BUILDER,
          useValue: async () => {
            throw new Error('no document');
          },
        },
      ],
    });

    const realtime = TestBed.inject(WorkspaceRealtime);

    realtime.start();
    await TestBed.inject(WorkspaceContextService).adopt(tenant);
    await settle();

    expect(realtime.conversationsLive()).toBe(false);
  });
});
