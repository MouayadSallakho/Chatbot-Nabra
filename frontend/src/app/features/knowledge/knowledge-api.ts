import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/**
 * What a document can currently do, exactly as the backend spells it.
 *
 * Three words rather than a pair of flags, because the three lead to different
 * actions: switch it back on, index it, or nothing at all. The backend derives
 * this from the document and the embedding profile in use, so a client can never
 * show "Ready" for a document whose index describes an older draft.
 */
export type KnowledgeReadiness = 'Ready' | 'NeedsIndex' | 'Disabled';

/** One document, in full. */
export interface KnowledgeDocument {
  readonly id: string;
  readonly tenantId: string;
  readonly title: string;
  readonly content: string;
  readonly isEnabled: boolean;
  readonly readiness: KnowledgeReadiness;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly indexedAt: string | null;
}

/** One document as the list publishes it. No content -- see the backend contract. */
export interface KnowledgeDocumentSummary {
  readonly id: string;
  readonly tenantId: string;
  readonly title: string;
  readonly isEnabled: boolean;
  readonly readiness: KnowledgeReadiness;
  readonly contentLength: number;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly indexedAt: string | null;
}

/** One page of documents. */
export interface KnowledgeDocumentPage {
  readonly items: readonly KnowledgeDocumentSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/**
 * What an index attempt did.
 *
 * `embeddingCalls` is published so the screen can say plainly that re-indexing
 * unchanged text cost nothing, rather than asserting it.
 */
export interface KnowledgeIndexResult {
  readonly document: KnowledgeDocument;
  readonly chunkCount: number;
  readonly embeddingCalls: number;
  readonly alreadyCurrent: boolean;
}

/** One document offered to an agent, and whether it is ticked. */
export interface AgentKnowledgeOption {
  readonly knowledgeDocumentId: string;
  readonly title: string;
  readonly isAssigned: boolean;
  readonly readiness: KnowledgeReadiness;
}

/** What an agent may draw on. */
export interface AgentKnowledge {
  readonly aiAgentId: string;
  readonly documents: readonly AgentKnowledgeOption[];
}

/**
 * One retrieval result.
 *
 * There is no vector here and there never will be: the embedding is several
 * kilobytes of numbers no screen can use, and publishing it would let anybody who
 * can read one document compare it arithmetically against every other.
 */
export interface KnowledgeSearchHit {
  readonly knowledgeDocumentId: string;
  readonly title: string;
  readonly ordinal: number;
  readonly excerpt: string;
  readonly rank: number;
  readonly similarity: number;
}

/** What a question would have retrieved. */
export interface KnowledgeSearchResult {
  readonly hits: readonly KnowledgeSearchHit[];
}

/** The page size the list asks for. Mirrors the backend default. */
export const DEFAULT_PAGE_SIZE = 20;

/**
 * The knowledge endpoints.
 *
 * <strong>Entity tags are captured here and nowhere else.</strong> Every call
 * that returns a document reads the `ETag` header and hands it back beside the
 * body, and every mutation requires one as `If-Match` -- indexing included,
 * because indexing is a write that replaces the chunk set and moves the version.
 * The signature will not compile without it, which is what makes the precondition
 * impossible to forget.
 *
 * <strong>The tag is opaque.</strong> Nothing here parses, compares, strips or
 * constructs one.
 */
@Injectable()
export class KnowledgeApi {
  private readonly http = inject(HttpClient);

  /** Reads one page of the workspace's documents. */
  list(
    tenantId: string,
    page: number = 1,
    pageSize: number = DEFAULT_PAGE_SIZE,
  ): Observable<KnowledgeDocumentPage> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);

    return this.http.get<KnowledgeDocumentPage>(this.knowledgeUrl(tenantId), { params });
  }

  /** Reads one document, with the version needed to change it. */
  get(tenantId: string, documentId: string): Observable<Versioned<KnowledgeDocument>> {
    return this.http
      .get<KnowledgeDocument>(this.documentUrl(tenantId, documentId), { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /**
   * Writes a new document.
   *
   * No `If-Match`: there is no previous version of something that does not exist
   * yet. It costs nothing -- creating a document does not index it.
   */
  create(
    tenantId: string,
    request: { title: string; content: string },
  ): Observable<Versioned<KnowledgeDocument>> {
    return this.http
      .post<KnowledgeDocument>(this.knowledgeUrl(tenantId), request, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Replaces a document's title and text, only if it is still at this version. */
  update(
    tenantId: string,
    documentId: string,
    request: { title: string; content: string },
    etag: string,
  ): Observable<Versioned<KnowledgeDocument>> {
    return this.conditional('PUT', this.documentUrl(tenantId, documentId), etag, request);
  }

  /**
   * Rebuilds the vectors behind a document.
   *
   * The one call in this client that costs money, and only when the content has
   * actually changed: an index of text that is already current makes no provider
   * call at all, and says so in the response.
   */
  index(
    tenantId: string,
    documentId: string,
    etag: string,
  ): Observable<Versioned<KnowledgeIndexResult>> {
    return this.http
      .request<KnowledgeIndexResult>('POST', `${this.documentUrl(tenantId, documentId)}/index`, {
        body: null,
        headers: { 'If-Match': etag },
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  /** Puts a document back into use. */
  enable(
    tenantId: string,
    documentId: string,
    etag: string,
  ): Observable<Versioned<KnowledgeDocument>> {
    return this.conditional('POST', `${this.documentUrl(tenantId, documentId)}/enable`, etag, null);
  }

  /** Takes a document out of use without discarding it or its index. */
  disable(
    tenantId: string,
    documentId: string,
    etag: string,
  ): Observable<Versioned<KnowledgeDocument>> {
    return this.conditional(
      'POST',
      `${this.documentUrl(tenantId, documentId)}/disable`,
      etag,
      null,
    );
  }

  /**
   * Reads which documents an agent may draw on.
   *
   * The tag it returns is the <em>agent's</em>, because changing the assignment
   * is a change to the agent's configuration and is written against that version.
   */
  agentKnowledge(tenantId: string, aiAgentId: string): Observable<Versioned<AgentKnowledge>> {
    return this.http
      .get<AgentKnowledge>(this.agentKnowledgeUrl(tenantId, aiAgentId), { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  /** Replaces which documents an agent may draw on. */
  replaceAgentKnowledge(
    tenantId: string,
    aiAgentId: string,
    knowledgeDocumentIds: readonly string[],
    etag: string,
  ): Observable<Versioned<AgentKnowledge>> {
    return this.http
      .request<AgentKnowledge>('PUT', this.agentKnowledgeUrl(tenantId, aiAgentId), {
        body: { knowledgeDocumentIds },
        headers: { 'If-Match': etag },
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  /**
   * Shows what an agent would retrieve for a question.
   *
   * Reads and changes nothing, so it sends no precondition and returns no tag.
   */
  search(tenantId: string, aiAgentId: string, question: string): Observable<KnowledgeSearchResult> {
    return this.http.post<KnowledgeSearchResult>(
      `${this.agentKnowledgeUrl(tenantId, aiAgentId)}/search`,
      { question },
    );
  }

  private conditional(
    method: 'PUT' | 'POST',
    url: string,
    etag: string,
    body: unknown,
  ): Observable<Versioned<KnowledgeDocument>> {
    return this.http
      .request<KnowledgeDocument>(method, url, {
        body,
        headers: { 'If-Match': etag },
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  private knowledgeUrl(tenantId: string): string {
    return `/api/tenants/${tenantId}/knowledge`;
  }

  private documentUrl(tenantId: string, documentId: string): string {
    return `${this.knowledgeUrl(tenantId)}/${documentId}`;
  }

  private agentKnowledgeUrl(tenantId: string, aiAgentId: string): string {
    return `/api/tenants/${tenantId}/ai-agents/${aiAgentId}/knowledge`;
  }
}
