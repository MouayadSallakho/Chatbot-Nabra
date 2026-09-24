import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import {
  AppNotification,
  NotificationSeverity,
  actionLabel,
  isInternalAction,
} from './notification-api';

/**
 * One notification, rendered.
 *
 * <strong>Never colour alone.</strong> Severity is an icon of a distinct shape
 * and a word for assistive technology; unread is a dot, bolder weight and the
 * word "Unread". Each survives a monochrome display and a screen reader.
 *
 * <strong>Text, not markup.</strong> Title and body are interpolated, so
 * `<script>` is shown as the characters it is. The action is a router link only
 * when the path is an internal route — anything else is simply not a link.
 */
@Component({
  selector: 'app-notification-item',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  template: `
    <article
      class="notification"
      [class.is-unread]="unread()"
      [attr.data-severity]="notification().severity"
      [attr.aria-labelledby]="titleId()"
      data-testid="notification"
    >
      <span class="notification-icon" [attr.data-severity]="notification().severity">
        <app-icon [name]="icon()" [strokeWidth]="1.75" />
      </span>

      <div class="notification-content">
        <p class="notification-heading">
          <span class="visually-hidden"
            >{{ severityWord() }}{{ unread() ? ', unread' : '' }}:
          </span>
          <span class="notification-title" [id]="titleId()">{{ notification().title }}</span>
          @if (unread()) {
            <span class="notification-dot" aria-hidden="true"></span>
          }
        </p>
        <p class="notification-body">{{ notification().body }}</p>

        <div class="notification-meta">
          <time
            class="notification-time"
            [attr.datetime]="notification().createdAt"
            [attr.title]="absolute()"
            >{{ relative() }}</time
          >

          @if (action(); as path) {
            <a
              class="notification-action"
              [routerLink]="path"
              (click)="opened.emit(notification())"
              data-testid="notification-action"
              >{{ label() }}<span class="visually-hidden">: {{ notification().title }}</span></a
            >
          }

          @if (unread()) {
            <button
              type="button"
              class="btn btn-ghost btn-sm notification-read"
              (click)="markedRead.emit(notification())"
              data-testid="mark-read"
            >
              <app-icon name="check" [strokeWidth]="2" />
              Mark as read<span class="visually-hidden">: {{ notification().title }}</span>
            </button>
          }
        </div>
      </div>
    </article>
  `,
  styles: `
    :host {
      display: block;
    }

    .notification {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr);
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
    }

    .notification-icon {
      display: inline-flex;
      margin-block-start: 2px;
      color: var(--info-fg);

      &[data-severity='Warning'] {
        color: var(--warning-fg);
      }

      &[data-severity='Critical'] {
        color: var(--danger-fg);
      }
    }

    .notification-content {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-inline-size: 0;
    }

    .notification-heading {
      display: flex;
      align-items: baseline;
      gap: var(--space-2);
      margin: 0;
    }

    .notification-title {
      font: var(--text-body);
      color: var(--text-primary);
      overflow-wrap: anywhere;
    }

    .is-unread .notification-title {
      font: var(--text-body-strong);
    }

    .notification-dot {
      flex: none;
      inline-size: 8px;
      block-size: 8px;
      border-radius: var(--radius-pill);
      background: var(--accent);
    }

    .notification-body {
      margin: 0;
      font: var(--text-small);
      color: var(--text-secondary);
      overflow-wrap: anywhere;
    }

    .notification-meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-3);
      font: var(--text-small);
    }

    .notification-time {
      color: var(--text-muted);
    }

    .notification-action {
      color: var(--accent);
      font-weight: 600;
    }

    .notification-read {
      gap: var(--space-1);
      // Optically aligned with the text beside it when it wraps onto its own line.
      margin-inline-start: calc(-1 * var(--space-3));
    }
  `,
})
export class NotificationItem {
  readonly notification = input.required<AppNotification>();

  /** Emitted when the action link is followed, so the caller can mark it read. */
  readonly opened = output<AppNotification>();

  readonly markedRead = output<AppNotification>();

  protected readonly unread = computed(() => this.notification().readAt === null);

  protected readonly titleId = computed(() => `notification-${this.notification().id}`);

  protected readonly action = computed(() => {
    const path = this.notification().actionPath;

    return isInternalAction(path) ? path : null;
  });

  protected readonly label = computed(() => actionLabel(this.notification().category));

  protected readonly icon = computed<IconName>(() => severityIcon(this.notification().severity));

  protected readonly severityWord = computed(() => this.notification().severity);

  protected readonly absolute = computed(() =>
    new Date(this.notification().createdAt).toLocaleString('en', {
      dateStyle: 'medium',
      timeStyle: 'short',
    }),
  );

  protected readonly relative = computed(() => relativeTime(this.notification().createdAt));
}

function severityIcon(severity: NotificationSeverity): IconName {
  switch (severity) {
    case 'Critical':
      return 'critical';
    case 'Warning':
      return 'alert';
    default:
      return 'info';
  }
}

/** "just now", "5 minutes ago", "3 days ago" — and a date beyond a week. */
export function relativeTime(iso: string, now: number = Date.now()): string {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
  const format = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

  if (Math.abs(seconds) < 45) {
    return 'just now';
  }

  const minutes = Math.round(seconds / 60);

  if (Math.abs(minutes) < 60) {
    return format.format(minutes, 'minute');
  }

  const hours = Math.round(minutes / 60);

  if (Math.abs(hours) < 24) {
    return format.format(hours, 'hour');
  }

  const days = Math.round(hours / 24);

  if (Math.abs(days) < 7) {
    return format.format(days, 'day');
  }

  return new Date(iso).toLocaleDateString('en', { dateStyle: 'medium' });
}
