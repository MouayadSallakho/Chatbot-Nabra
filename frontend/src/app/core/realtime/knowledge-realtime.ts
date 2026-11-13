import { Injectable, inject } from '@angular/core';
import { KnowledgeChanged, WorkspaceRealtime } from './workspace-realtime';

export type { KnowledgeChanged } from './workspace-realtime';

/**
 * What the knowledge screens hear over the page's one connection (Phase 38.8, BA-11).
 *
 * The server says that a document's indexing started or settled, with its id and
 * nothing else; the screen re-reads through the ordinary authorised API. Listening
 * is what joins the knowledge group, so a screen listens from entering to leaving,
 * and the connection holds the group only meanwhile.
 */
@Injectable({ providedIn: 'root' })
export class KnowledgeRealtime {
  private readonly owner = inject(WorkspaceRealtime);

  /** Calls back on every knowledge change in this workspace. Returns the unsubscribe. */
  listen(tenantId: string, onChanged: KnowledgeChanged): () => void {
    return this.owner.onKnowledge((documentId) => {
      // A screen still on the page during a workspace switch hears nothing of the next one.
      if (this.owner.tenantId() === tenantId) {
        onChanged(documentId);
      }
    });
  }
}
