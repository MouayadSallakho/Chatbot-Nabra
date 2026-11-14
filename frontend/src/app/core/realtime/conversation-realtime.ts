import { Injectable, inject } from '@angular/core';
import { ConversationChanged, WorkspaceRealtime } from './workspace-realtime';

export type { ConversationChanged } from './workspace-realtime';

/**
 * The inbox's view of the page's one realtime connection (`WorkspaceRealtime`).
 *
 * Screens listen for as long as they are on the page; the connection itself
 * belongs to the shell and outlives them, because the bell uses it too.
 */
@Injectable({ providedIn: 'root' })
export class ConversationRealtime {
  private readonly owner = inject(WorkspaceRealtime);

  /** Whether updates are arriving. False means stale-but-correct, never broken. */
  readonly live = this.owner.conversationsLive;

  /** Calls back on every change in this workspace. Returns the unsubscribe. */
  listen(tenantId: string, onChanged: ConversationChanged): () => void {
    return this.owner.onConversation((conversationId) => {
      // A screen still on the page during a workspace switch hears nothing of the next one.
      if (this.owner.tenantId() === tenantId) {
        onChanged(conversationId);
      }
    });
  }

  /**
   * Calls back when this member marked every conversation in this workspace
   * read, from any of their tabs (`conversationsRead`, Phase 39.1). Returns the
   * unsubscribe.
   */
  listenRead(tenantId: string, onRead: () => void): () => void {
    return this.owner.onConversationsRead((workspace) => {
      // The event names its workspace: another workspace's is not this list's news.
      if (
        this.owner.tenantId() === tenantId &&
        workspace?.toLowerCase() === tenantId.toLowerCase()
      ) {
        onRead();
      }
    });
  }
}
