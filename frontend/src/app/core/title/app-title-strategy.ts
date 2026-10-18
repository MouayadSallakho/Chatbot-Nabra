import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** Appended so a browser tab still identifies the product. */
const PRODUCT_NAME = 'OmniAI';

/**
 * Builds the browser tab title from the active route.
 *
 * Angular's default strategy sets the tab to the route title alone, which would
 * leave a reader with several tabs open looking at "Overview" and "Settings"
 * with nothing saying which product they belong to. Appending the product name
 * costs one class and fixes that.
 *
 * The route title is used as-is otherwise, so the tab, the top bar and the route
 * definition all read from a single string.
 */
@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);

    this.title.setTitle(
      page
        ? `${page} · ${PRODUCT_NAME}`
        : $localize`:@@title.productAdmin:${PRODUCT_NAME}:product: Admin`,
    );
  }
}
