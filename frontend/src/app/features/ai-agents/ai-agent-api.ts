import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** The lifecycle states the backend publishes, spelled exactly as it spells them. */
export type AiAgentStatus = 'Draft' | 'Active' | 'Disabled';

/** One agent, in full. What the detail endpoint returns. */
export interface AiAgent {
  readonly id: string;
  readonly tenantId: string;
  readonly name: string;
  readonly instructions: string;
  readonly status: AiAgentStatus;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** One agent as the list publishes it. No instructions -- see the backend contract. */
export interface AiAgentSummary {
  readonly id: string;
  readonly tenantId: string;
  readonly name: string;
  readonly status: AiAgentStatus;
  readonly createdAt: string;
  readonly updatedAt: string;
}

/** One page of agents. */
export interface AiAgentPage {
  readonly items: readonly AiAgentSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/**
 * An agent together with the entity tag it was read at.
 *
 * The two travel together because they are only useful together: the agent is
 * what to show, and the tag is what the next write must present. A caller that
 * has one without the other cannot safely save.
 */
export interface VersionedAiAgent {
  readonly agent: AiAgent;
  readonly etag: string;
}

/** What the preview endpoint answers with. One field. */
export interface AiAgentPreview {
  readonly reply: string;
}

/** How many agents a page may hold. Mirrors the backend's own bound. */
export const MAX_PAGE_SIZE = 100;

/** The page size the list asks for. */
export const DEFAULT_PAGE_SIZE = 20;

/**
 * The AI agent endpoints.
 *
 * <strong>Entity tags are captured here and nowhere else.</strong> Every call
 * that returns an agent reads the `ETag` response header and hands it back
 * beside the body, and every mutation requires the caller to supply one as
 * `If-Match`. That is what makes the precondition impossible to forget: the
 * method signature will not compile without it.
 *
 * <strong>The tag is opaque.</strong> Nothing here parses it, compares it,
 * strips its quotes or constructs one. It is a string the server produced and
 * the server will interpret; the moment a client starts deriving them, the
 * concurrency guarantee is gone.
 *
 * <strong>Preview takes no tag.</strong> It reads the agent and changes nothing,
 * so there is no write to protect and no version to advance. Requiring one would
 * teach callers the wrong thing about what the endpoint does.
 */
@Injectable()
export class AiAgentApi {
  private readonly http = inject(HttpClient);

  /** Reads one page of the workspace's agents. */
  list(
    tenantId: string,
    page: number = 1,
    pageSize: number = DEFAULT_PAGE_SIZE,
  ): Observable<AiAgentPage> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);

    return this.http.get<AiAgentPage>(this.agentsUrl(tenantId), { params });
  }

  /** Reads one agent, with the tag needed to change it. */
  get(tenantId: string, aiAgentId: string): Observable<VersionedAiAgent> {
    return this.http
      .get<AiAgent>(this.agentUrl(tenantId, aiAgentId), { observe: 'response' })
      .pipe(map(versioned));
  }

  /**
   * Creates an agent.
   *
   * No `If-Match`: there is no previous version of something that does not exist
   * yet. The tag that comes back is what lets the editor save immediately
   * without re-reading.
   */
  create(
    tenantId: string,
    request: { name: string; instructions: string },
  ): Observable<VersionedAiAgent> {
    return this.http
      .post<AiAgent>(this.agentsUrl(tenantId), request, { observe: 'response' })
      .pipe(map(versioned));
  }

  /** Renames an agent, only if it is still at the given version. */
  rename(
    tenantId: string,
    aiAgentId: string,
    name: string,
    etag: string,
  ): Observable<VersionedAiAgent> {
    return this.conditional('PUT', `${this.agentUrl(tenantId, aiAgentId)}/name`, etag, { name });
  }

  /** Replaces an agent's instructions, only if it is still at the given version. */
  updateInstructions(
    tenantId: string,
    aiAgentId: string,
    instructions: string,
    etag: string,
  ): Observable<VersionedAiAgent> {
    return this.conditional('PUT', `${this.agentUrl(tenantId, aiAgentId)}/instructions`, etag, {
      instructions,
    });
  }

  /** Activates an agent, only if it is still at the given version. */
  activate(tenantId: string, aiAgentId: string, etag: string): Observable<VersionedAiAgent> {
    return this.conditional('POST', `${this.agentUrl(tenantId, aiAgentId)}/activate`, etag, null);
  }

  /** Disables an agent, only if it is still at the given version. */
  disable(tenantId: string, aiAgentId: string, etag: string): Observable<VersionedAiAgent> {
    return this.conditional('POST', `${this.agentUrl(tenantId, aiAgentId)}/disable`, etag, null);
  }

  /**
   * Asks the agent to answer one message.
   *
   * Stateless and single-turn, because that is what the endpoint is. It sends no
   * precondition and returns no tag, so a preview can never advance the version
   * the editor is holding.
   */
  preview(tenantId: string, aiAgentId: string, message: string): Observable<AiAgentPreview> {
    return this.http.post<AiAgentPreview>(`${this.agentUrl(tenantId, aiAgentId)}/preview`, {
      message,
    });
  }

  /** Every mutation, with its precondition attached. */
  private conditional(
    method: 'PUT' | 'POST',
    url: string,
    etag: string,
    body: unknown,
  ): Observable<VersionedAiAgent> {
    return this.http
      .request<AiAgent>(method, url, {
        body,
        headers: { 'If-Match': etag },
        observe: 'response',
      })
      .pipe(map(versioned));
  }

  private agentsUrl(tenantId: string): string {
    return `/api/tenants/${tenantId}/ai-agents`;
  }

  private agentUrl(tenantId: string, aiAgentId: string): string {
    return `${this.agentsUrl(tenantId)}/${aiAgentId}`;
  }
}

/**
 * Pairs a response body with its entity tag.
 *
 * A missing tag is a broken contract rather than something to paper over: every
 * endpoint that returns an agent sets one. Failing loudly here is far better
 * than returning an empty string that would later be sent as `If-Match` and
 * rejected with a confusing 412.
 */
function versioned(response: HttpResponse<AiAgent>): VersionedAiAgent {
  const etag = response.headers.get('ETag');

  if (!etag) {
    throw new Error('The agent response carried no ETag.');
  }

  return { agent: response.body as AiAgent, etag };
}
