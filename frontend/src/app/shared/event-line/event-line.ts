import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * An event between messages (calm-glass.md §6): a centred line with words.
 * A failed turn is coral; a takeover is the team's gold, a return to the AI
 * emerald; anything else is quiet. `role="note"`, and the words carry the
 * meaning: the rule on either side is decorative.
 *
 * ```html
 * <app-event-line tone="failed">The AI could not reply · 10:41</app-event-line>
 * ```
 */
@Component({
  selector: 'app-event-line',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'note',
    '[class]': "'event-line' + (tone() === 'quiet' ? '' : ' event-line-' + tone())",
  },
  template: `<span class="event-line-words"><ng-content /></span>`,
})
export class EventLine {
  readonly tone = input<'quiet' | 'failed' | 'team' | 'ai'>('quiet');
}
