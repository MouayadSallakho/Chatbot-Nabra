import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  Event as RouterEvent,
  NavigationCancel,
  NavigationCancellationCode,
  NavigationEnd,
  NavigationError,
  NavigationSkipped,
  Router,
  RouterOutlet,
} from '@angular/router';
import { filter, map, take } from 'rxjs';
import { BootState } from '@app/core/boot/boot-state';

/**
 * Whether a navigation event means the first page has landed (or never will).
 *
 * A redirect is not an ending: a guard sending a signed-out reader to sign-in
 * cancels one navigation and starts the next, and the loading state has to
 * stay up through both. Every other ending removes it, so it can never stick.
 */
export function settlesFirstNavigation(event: RouterEvent): boolean {
  return (
    event instanceof NavigationEnd ||
    event instanceof NavigationError ||
    event instanceof NavigationSkipped ||
    (event instanceof NavigationCancel && event.code !== NavigationCancellationCode.Redirect)
  );
}

/**
 * The application root.
 *
 * Intentionally almost empty. The landmarks, the navigation and the layout all
 * belong to the shell, which is a routed component -- so a future phase can put
 * a sign-in screen outside the shell without unpicking anything here.
 *
 * Keeping this component with nothing in it is a deliberate guard: the root is
 * the easiest place in an Angular application to accumulate global state,
 * cross-cutting subscriptions and "temporary" markup that never leaves.
 *
 * The one thing it adds (Phase 36.5) is the end of the loading state: the
 * lockup index.html painted stays on screen until the first page has landed,
 * because the first page is a lazy chunk and the moment between the two was a
 * blank frame. It is gone for good after that.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, BootState],
  templateUrl: './app.html',
})
export class App {
  protected readonly landed = toSignal(
    inject(Router).events.pipe(
      filter(settlesFirstNavigation),
      take(1),
      map(() => true),
    ),
    { initialValue: false },
  );
}
