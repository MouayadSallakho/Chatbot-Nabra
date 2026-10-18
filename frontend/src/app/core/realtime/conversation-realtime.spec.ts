import { TestBed } from '@angular/core/testing';
import type { HubConnectionBuilder } from '@microsoft/signalr';
import { ConversationRealtime, HUB_CONNECTION_BUILDER } from './conversation-realtime';

// A stand-in for the SignalR client: the test is about how many connections
// the service opens and who hears about a change, not about the transport.
const built: FakeConnection[] = [];

class FakeConnection {
  state = 'Disconnected';
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  readonly invoked: unknown[][] = [];

  on(name: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(name, handler);
  }

  onreconnected(): void {}
  onreconnecting(): void {}
  onclose(): void {}

  async start(): Promise<void> {
    this.state = 'Connected';
  }

  async stop(): Promise<void> {
    this.state = 'Disconnected';
  }

  async invoke(...args: unknown[]): Promise<void> {
    this.invoked.push(args);
  }
}

/**
 * A stand-in for SignalR's builder, handed to the service through its
 * injection token. Not a module mock: specs share one module registry in this
 * runner, so a `vi.mock` of `@microsoft/signalr` depended on which spec loaded
 * the service first (Phase 36.7, revision 1).
 */
class FakeBuilder {
  withUrl(): this {
    return this;
  }

  withAutomaticReconnect(): this {
    return this;
  }

  configureLogging(): this {
    return this;
  }

  build(): FakeConnection {
    const connection = new FakeConnection();

    built.push(connection);

    return connection;
  }
}

describe('ConversationRealtime', () => {
  const tenant = '11111111-1111-7111-8111-111111111111';

  beforeEach(() => {
    built.length = 0;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: HUB_CONNECTION_BUILDER,
          useValue: () => new FakeBuilder() as unknown as HubConnectionBuilder,
        },
      ],
    });
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('shares one connection between screens that listen in the same tick', async () => {
    // The inbox's list and its open conversation subscribe together.
    const realtime = TestBed.inject(ConversationRealtime);
    const heard: string[] = [];

    realtime.listen(tenant, (id) => heard.push(`list:${id}`));
    realtime.listen(tenant, (id) => heard.push(`detail:${id}`));
    await settle();

    expect(built).toHaveLength(1);
    expect(built[0].invoked).toEqual([['Subscribe', tenant]]);
    expect(realtime.live()).toBe(true);

    built[0].handlers.get('conversationChanged')!('c1');

    expect(heard).toEqual(['list:c1', 'detail:c1']);
  });

  it('closes the connection when the last screen stops listening', async () => {
    const realtime = TestBed.inject(ConversationRealtime);
    const stopList = realtime.listen(tenant, () => undefined);
    const stopDetail = realtime.listen(tenant, () => undefined);

    await settle();
    stopDetail();
    await settle();

    expect(built[0].state).toBe('Connected');

    stopList();
    await settle();

    expect(built[0].state).toBe('Disconnected');
    expect(realtime.live()).toBe(false);
  });
});
