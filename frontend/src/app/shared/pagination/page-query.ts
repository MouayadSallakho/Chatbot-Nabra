import { inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

/** The query parameter every paged list keeps its page in. */
export const PAGE_QUERY_PARAM = 'page';

/**
 * The page a list should open on, read from its own address.
 *
 * Kept in the URL rather than only in memory, so that reloading, sharing a link
 * and -- most of all -- coming back from a detail screen return the reader to
 * the page they left, instead of to page one.
 *
 * Anything that is not a whole number of at least one is treated as page one:
 * the address is typed by people, and a bad one must not break the list.
 */
export function initialPage(): number {
  const raw = inject(ActivatedRoute).snapshot.queryParamMap.get(PAGE_QUERY_PARAM);
  const page = Number(raw);

  return Number.isInteger(page) && page >= 1 ? page : 1;
}

/**
 * Returns a function that records a list's page in its address.
 *
 * The history entry is replaced rather than pushed: Back should leave the list,
 * not step through every page the reader turned. Page one is written as no
 * parameter at all, so the list's plain address stays its canonical one.
 */
export function pageRecorder(): (page: number) => void {
  const router = inject(Router);
  const route = inject(ActivatedRoute);

  return (page: number) => {
    void router.navigate([], {
      relativeTo: route,
      queryParams: { [PAGE_QUERY_PARAM]: page > 1 ? page : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  };
}
