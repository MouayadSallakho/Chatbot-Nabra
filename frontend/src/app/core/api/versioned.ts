import { HttpResponse } from '@angular/common/http';

/**
 * Something the server returned, together with the entity tag it was read at.
 *
 * The two travel together because they are only useful together: the value is
 * what to show, and the tag is what the next write must present. A caller that
 * has one without the other cannot safely save.
 *
 * <strong>Generic because Phase 13 produced a second conditional resource, and
 * no sooner.</strong> Phase 11 wrote this shape by hand for AI agents. Tenant
 * roles are the second, and the choice was between a parallel
 * `VersionedRole` or one type used twice — and two spellings of an identical
 * idea is the thing that becomes three. The hand-written pair was replaced by
 * this and its name retired.
 *
 * It is one interface and one function, not a framework: nothing implements
 * anything to participate, and the generic parameter carries no constraint,
 * because an entity tag has no resource-specific content.
 */
export interface Versioned<T> {
  readonly value: T;
  readonly etag: string;
}

/**
 * Pairs a response body with its entity tag.
 *
 * A missing tag is a broken contract rather than something to paper over: every
 * endpoint that returns a conditional resource sets one. Failing loudly here is
 * far better than returning an empty string that would later be sent as
 * `If-Match` and rejected with a confusing `412` nobody could explain.
 */
export function withEntityTag<T>(response: HttpResponse<T>): Versioned<T> {
  const etag = response.headers.get('ETag');

  if (!etag) {
    throw new Error('The response carried no ETag.');
  }

  return { value: response.body as T, etag };
}
