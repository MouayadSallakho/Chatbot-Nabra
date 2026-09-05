import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

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
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  templateUrl: './app.html',
})
export class App {}
