import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * Application shell. Holds the accessibility landmarks and the routed outlet,
 * and nothing else -- feature layout belongs to the features themselves.
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('OmniAI Admin');
}
