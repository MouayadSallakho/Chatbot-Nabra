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

/**
 * The formats a file upload accepts, exactly as the backend names them.
 *
 * Deliberately not a MIME type and deliberately not a file extension: the
 * backend decides what a file actually is by looking inside it, and this is the
 * answer it reached rather than what the name claimed.
 */
export type KnowledgeFileType = 'Pdf' | 'Docx' | 'Text' | 'Csv';

/**
 * Where a document's text came from.
 *
 * <strong>There is no file here and there never will be.</strong> The uploaded
 * bytes are read once, during the request that carried them, and discarded; what
 * survives is the extracted text and this description of the file it came out
 * of. Nothing in this platform can hand the original file back, because nothing
 * in this platform kept it.
 */
export interface KnowledgeSource {
  readonly fileName: string;
  readonly fileType: KnowledgeFileType;
  readonly byteLength: number;
  readonly extractedAt: string;
}

/**
 * What the newest index attempt did (Phase 37.5a).
 *
 * `status` is `Started` (indexing now), `Succeeded`, `Failed` or `Unknown` (the
 * process stopped before it could say). `failureCode` is a stable reason, never a
 * provider's message.
 */
export interface KnowledgeIndexAttempt {
  readonly status: string;
  readonly failureCode: string | null;
  readonly startedAt: string;
  readonly completedAt: string | null;
}

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

  /** Null for a document somebody typed. */
  readonly source: KnowledgeSource | null;

  /**
   * On the reads only. Null means never attempted; absent (a write's answer) says
   * nothing, so a write can never claim "never attempted" for one that was.
   */
  readonly lastIndexAttempt?: KnowledgeIndexAttempt | null;
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

  /**
   * The format the document was read from, or null when it was typed.
   *
   * Only the type, not the filename: a filename can itself be sensitive, and a
   * list of two hundred rows is not where it needs to be read.
   */
  readonly sourceFileType: KnowledgeFileType | null;

  /** The uploaded file's size in bytes, or null when the document was typed. */
  readonly sourceByteLength: number | null;

  /** Null means never attempted. */
  readonly lastIndexAttempt: KnowledgeIndexAttempt | null;
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

  /**
   * Uploads a file and stores the text read out of it as a new document.
   *
   * <strong>Multipart, because the file is sent as a file.</strong> Base64 inside
   * a JSON body would be a third larger on the wire for no gain. `FormData` also
   * means the browser sets the boundary itself -- so nothing here sets a
   * `Content-Type`, which would overwrite it with a header missing the boundary
   * and produce a body the server cannot parse.
   *
   * No `If-Match`: nothing exists yet to be stale. It costs nothing -- extracting
   * text is not indexing.
   */
  uploadFile(
    tenantId: string,
    file: File,
    title?: string,
  ): Observable<Versioned<KnowledgeDocument>> {
    const body = new FormData();

    body.append('file', file, file.name);

    if (title && title.trim().length > 0) {
      body.append('title', title.trim());
    }

    return this.http
      .post<KnowledgeDocument>(`${this.knowledgeUrl(tenantId)}/files`, body, {
        observe: 'response',
      })
      .pipe(map(withEntityTag));
  }

  /**
   * Replaces the file behind a document, only if it is still at this version.
   *
   * A successful replacement changes the text, so the document stops being
   * indexed and needs indexing again. That is the server's decision and is
   * visible in the response it returns.
   */
  replaceSource(
    tenantId: string,
    documentId: string,
    file: File,
    etag: string,
  ): Observable<Versioned<KnowledgeDocument>> {
    const body = new FormData();

    body.append('file', file, file.name);

    return this.http
      .request<KnowledgeDocument>('PUT', `${this.documentUrl(tenantId, documentId)}/source`, {
        body,
        headers: { 'If-Match': etag },
        observe: 'response',
      })
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
