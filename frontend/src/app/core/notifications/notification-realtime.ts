import { Injectable, inject } from '@angular/core';
import { WorkspaceRealtime } from '@app/core/realtime/workspace-realtime';

/**
 * The bell's view of the page's one realtime connection (`WorkspaceRealtime`).
 *
 * <strong>Structural only.</strong> What arrives is "something changed in this
 * workspace" and nothing else: no title, no body, no count. The listener re-reads
 * through the authorised HTTP API, which stays the only source of what a
 * notification says.
 */
@Injectable({ providedIn: 'root' })
export class NotificationRealtime {
  private readonly owner = inject(WorkspaceRealtime);

  private unsubscribe: (() => void) | null = null;

  /** Whether updates are arriving. False means stale-but-correct, never broken. */
  readonly live = this.owner.notificationsLive;

  /** Listens to one workspace. A later call replaces the listener. */
  listen(tenantId: string, onChanged: () => void): void {
    this.unsubscribe?.();
    this.unsubscribe = this.owner.onNotifications(() => {
      if (this.owner.tenantId() === tenantId) {
        onChanged();
      }
    });
  }

  /** Stops listening. The connection itself follows the workspace, not this listener. */
  async stop(): Promise<void> {
    this.unsubscribe?.();
    this.unsubscribe = null;
  }
}
