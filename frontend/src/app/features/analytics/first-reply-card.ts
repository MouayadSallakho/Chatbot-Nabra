import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { FirstReplyTimes } from './analytics-api';

@Component({
  selector: 'app-first-reply-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'card card-body',
    'data-testid': 'first-reply',
    '[attr.aria-busy]': 'times() === null',
  },
  template: `
    <h2 class="panel-title" i18n="@@analytics.firstReply.heading">Time to first reply</h2>
    <p class="text-caption" i18n="@@analytics.firstReply.note">
      From the first customer message, for conversations started and answered in the last
      {{ locale.number(days()) }} days.
    </p>
    @if (times(); as sample) {
      <table>
        <thead>
          <tr>
            <th scope="col" i18n="@@analytics.firstReply.responder">First responder</th>
            <th scope="col" i18n="@@analytics.firstReply.median">Median wait</th>
            <th scope="col" i18n="@@analytics.firstReply.count">Conversations</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <th scope="row" i18n="@@analytics.firstReply.ai">AI</th>
            <td>{{ duration(sample.aiMedianSeconds) }}</td>
            <td>{{ locale.number(sample.aiConversations) }}</td>
          </tr>
          <tr>
            <th scope="row" i18n="@@analytics.firstReply.team">Team</th>
            <td>{{ duration(sample.teamMedianSeconds) }}</td>
            <td>{{ locale.number(sample.teamConversations) }}</td>
          </tr>
        </tbody>
      </table>
    } @else {
      <div class="skeleton" aria-hidden="true"></div>
    }
  `,
  styles: `
    :host {
      min-inline-size: 0;
    }
    .panel-title {
      font: var(--text-title-3);
    }
    .skeleton {
      min-block-size: 6rem;
    }
    table {
      inline-size: 100%;
      font: var(--text-small);
    }
    th,
    td {
      text-align: start;
      padding-block: var(--space-2);
    }
    td {
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class FirstReplyCard {
  readonly times = input.required<FirstReplyTimes | null>();
  readonly days = input.required<number>();
  protected readonly locale = inject(LocaleFormat);

  protected duration(seconds: number | null): string {
    if (seconds == null) return $localize`:@@analytics.firstReply.noData:No data`;
    if (seconds > 0 && seconds < 1)
      return $localize`:@@analytics.firstReply.underSecond:Under 1 second`;
    const unit = seconds < 60 ? 'second' : seconds < 3600 ? 'minute' : 'hour';
    const divisor = unit === 'hour' ? 3600 : unit === 'minute' ? 60 : 1;
    return this.locale.number(seconds / divisor, {
      style: 'unit',
      unit,
      unitDisplay: 'short',
      maximumFractionDigits: 1,
    });
  }
}
