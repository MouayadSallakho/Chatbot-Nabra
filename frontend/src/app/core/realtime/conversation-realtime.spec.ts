import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ConversationRealtime } from './conversation-realtime';
import { WorkspaceRealtime } from './workspace-realtime';

/** The facade: which workspace a screen hears, and what `live` reads. */
describe('ConversationRealtime', () => {
  let owner: FakeOwner;

  class FakeOwner {
    current: string | null = 't1';
    listeners = new Set<(id: string | null) => void>();
    conversationsLive = signal(true);

    tenantId(): string | null {
      return this.current;
    }

    onConversation(listener: (id: string | null) => void): () => void {
      this.listeners.add(listener);

      return () => this.listeners.delete(listener);
    }

    fire(id: string | null): void {
      for (const listener of this.listeners) {
        listener(id);
      }
    }

    readListeners = new Set<(tenantId: string | null) => void>();

    onConversationsRead(listener: (tenantId: string | null) => void): () => void {
      this.readListeners.add(listener);

      return () => this.readListeners.delete(listener);
    }

    fireRead(tenantId: string | null): void {
      for (const listener of this.readListeners) {
        listener(tenantId);
      }
    }
  }

  beforeEach(() => {
    owner = new FakeOwner();
    TestBed.configureTestingModule({
      providers: [{ provide: WorkspaceRealtime, useValue: owner }],
    });
  });

  it('passes on the events of the workspace the screen asked about', () => {
    const heard: (string | null)[] = [];

    TestBed.inject(ConversationRealtime).listen('t1', (id) => heard.push(id));
    owner.fire('c1');
    owner.fire(null);

    expect(heard).toEqual(['c1', null]);
  });

  it('hears nothing of another workspace, during a switch', () => {
    const heard: (string | null)[] = [];

    TestBed.inject(ConversationRealtime).listen('t1', (id) => heard.push(id));
    owner.current = 't2';
    owner.fire('c1');

    expect(heard).toEqual([]);
  });

  it('stops listening when told, and reads the connection state from its owner', () => {
    const realtime = TestBed.inject(ConversationRealtime);
    const stop = realtime.listen('t1', () => undefined);

    expect(owner.listeners.size).toBe(1);
    expect(realtime.live()).toBe(true);

    stop();

    expect(owner.listeners.size).toBe(0);
  });

  describe('conversationsRead (Phase 39.1)', () => {
    it("passes on the member's mark-all-read for this workspace, whatever its letter case", () => {
      owner.current = 'aa11';
      const heard = vi.fn();

      TestBed.inject(ConversationRealtime).listenRead('aa11', heard);
      owner.fireRead('AA11');

      expect(heard).toHaveBeenCalledTimes(1);
    });

    it('ignores another workspace, a switch in progress and an empty payload', () => {
      const heard = vi.fn();
      const stop = TestBed.inject(ConversationRealtime).listenRead('t1', heard);

      owner.fireRead('t2');
      owner.fireRead(null);
      owner.current = 't2';
      owner.fireRead('t1');

      expect(heard).not.toHaveBeenCalled();

      stop();

      expect(owner.readListeners.size).toBe(0);
    });
  });
});
