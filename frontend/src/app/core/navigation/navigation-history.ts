import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/**
 * Remembers where the reader was one navigation ago.
 *
 * It exists for one job: a detail screen's back link should return to the list
 * the reader came from -- the same page of it -- rather than to page one. The
 * list keeps its page in its own address, so returning to that address is all it
 * takes; nothing about the list's state is stored here.
 *
 * Deliberately not `history.back()`. That leaves the application when the
 * detail page was opened from a bookmark or a new tab, and it cannot tell the
 * list apart from wherever else the reader happened to be.
 */
@Injectable({ providedIn: 'root' })
export class NavigationHistory {
  private previous: string | null = null;
  private current: string | null = null;

  constructor() {
    inject(Router)
      .events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe((event) => {
        // A navigation that only rewrote the query of the same screen -- a list
        // recording its page -- is not a new place to come back from.
        if (this.current !== null && pathOf(this.current) === pathOf(event.urlAfterRedirects)) {
          this.current = event.urlAfterRedirects;

          return;
        }

        this.previous = this.current;
        this.current = event.urlAfterRedirects;
      });
  }

  /**
   * The address a back link to `path` should open: the exact URL the reader
   * left, when that was `path`, and otherwise `path` itself.
   */
  returnUrl(path: string): string {
    return this.previous !== null && pathOf(this.previous) === pathOf(path) ? this.previous : path;
  }
}

function pathOf(url: string): string {
  return url.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
}
