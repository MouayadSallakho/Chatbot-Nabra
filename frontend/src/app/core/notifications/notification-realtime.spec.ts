import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WorkspaceRealtime } from '@app/core/realtime/workspace-realtime';
import { NotificationRealtime } from './notification-realtime';

/** The facade: one listener, for one workspace, on a connection it does not own. */
describe('NotificationRealtime', () => {
  let owner: FakeOwner;

  class FakeOwner {
    current: string | null = 't1';
    listeners = new Set<() => void>();
    notificationsLive = signal(true);

    tenantId(): string | null {
      return this.current;
    }

    onNotifications(listener: () => void): () => void {
      this.listeners.add(listener);

      return () => this.listeners.delete(listener);
    }

    fire(): void {
      for (const listener of this.listeners) {
        listener();
      }
    }
  }

  beforeEach(() => {
    owner = new FakeOwner();
    TestBed.configureTestingModule({
      providers: [{ provide: WorkspaceRealtime, useValue: owner }],
    });
  });

  it('keeps one listener: a later call replaces the earlier one', () => {
    const realtime = TestBed.inject(NotificationRealtime);
    const first = vi.fn();
    const second = vi.fn();

    realtime.listen('t1', first);
    realtime.listen('t1', second);
    owner.fire();

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });

  it('hears nothing of another workspace', () => {
    const heard = vi.fn();

    TestBed.inject(NotificationRealtime).listen('t1', heard);
    owner.current = 't2';
    owner.fire();

    expect(heard).not.toHaveBeenCalled();
  });

  it('removes its listener on stop, and leaves the connection to its owner', async () => {
    const realtime = TestBed.inject(NotificationRealtime);

    realtime.listen('t1', () => undefined);
    await realtime.stop();

    expect(owner.listeners.size).toBe(0);
    expect(realtime.live()).toBe(true);
  });
});
