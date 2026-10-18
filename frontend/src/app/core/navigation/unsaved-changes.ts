import { DOCUMENT, DestroyRef, inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';

/** A screen that can hold edits the reader has not saved yet. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/** The question asked before unsaved edits are thrown away. */
export const UNSAVED_CHANGES_QUESTION = $localize`:@@unsaved.leaveQuestion:You have unsaved changes on this page. Leave and discard them?`;

/** The question asked before a reload replaces unsaved edits with the saved version. */
export const RELOAD_REPLACES_CHANGES_QUESTION = $localize`:@@unsaved.reloadQuestion:Reloading replaces your unsaved changes with the latest saved version. Continue?`;

/**
 * Asks before leaving a screen with unsaved edits, and only then.
 *
 * A native confirmation rather than the product's dialog: the browser's own
 * leave-page prompt (below) is native too, and one question should look the
 * same whichever way the reader tried to leave. A screen with nothing unsaved
 * is left without a word.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  !component.hasUnsavedChanges() || globalThis.confirm(UNSAVED_CHANGES_QUESTION);

/**
 * Asks the browser to warn before the tab is closed or reloaded while there are
 * unsaved edits. Call from a constructor; the listener goes when the screen does.
 *
 * The browser writes its own wording for this prompt and ignores ours, which is
 * why only `preventDefault` is called.
 */
export function protectUnsavedChanges(isDirty: () => boolean): void {
  const view = inject(DOCUMENT).defaultView;

  if (!view) {
    return;
  }

  const listener = (event: BeforeUnloadEvent) => {
    if (isDirty()) {
      event.preventDefault();
    }
  };

  view.addEventListener('beforeunload', listener);
  inject(DestroyRef).onDestroy(() => view.removeEventListener('beforeunload', listener));
}
