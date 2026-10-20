import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { formatDate, formatRelative } from '@app/core/i18n/locale-format';
import {
  AppNotification,
  NotificationSeverity,
  actionLabel,
  isInternalAction,
} from './notification-api';
import { categoryWord, notificationText } from './notification-words';

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
          <span class="visually-hidden">{{ severityLabel() }} </span>
          <!-- In the reader's language; a kind this build does not know keeps the server's words. -->
          <span class="notification-title" dir="auto" [id]="titleId()">{{ text().title }}</span>
        </p>
        <p class="notification-body" dir="auto">{{ text().body }}</p>

        <div class="notification-meta">
          @if (unread()) {
            <!-- Unread is a dot, a heavier title and this word: never one of them alone. -->
            <span class="notification-unread" aria-hidden="true">
              <span class="notification-dot"></span>
              <ng-container i18n="@@notifications.unreadWord">Unread</ng-container>
            </span>
          }
          <span class="notification-category">{{ category() }}</span>
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
              >{{ label() }}<span class="visually-hidden">: {{ text().title }}</span></a
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
              <ng-container i18n="@@notifications.markRead">Mark as read</ng-container
              ><span class="visually-hidden">: {{ text().title }}</span>
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
      padding: var(--space-4) var(--space-4) var(--space-3);
    }

    .notification-icon {
      display: inline-flex;
      // token-lint-ignore: optical alignment of the 20px icon with the title's first line
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

    .is-unread {
      box-shadow: inset 3px 0 0 var(--action);
    }

    :host-context([dir='rtl']) .is-unread {
      box-shadow: inset -3px 0 0 var(--action);
    }

    .notification-unread {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      color: var(--text-primary);
      font-weight: 600;
    }

    .notification-dot {
      flex: none;
      inline-size: 8px;
      block-size: 8px;
      border-radius: var(--radius-pill);
      // Unread is news, not an alarm, and not a voice: ink.
      background: var(--action);
    }

    .notification-category {
      color: var(--text-muted);
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

    // A link, so ink and underlined; its target is taller than its text
    // (WCAG 2.5.8 asks for 24px, the panel gives 28px).
    .notification-action {
      display: inline-flex;
      align-items: center;
      min-block-size: 1.75rem;
      color: var(--link);
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

  /**
   * How the time is said: relative in the bell ("5 minutes ago"), the time of
   * day in the history, where each day already has its own heading.
   */
  readonly timeStyle = input<'relative' | 'time'>('relative');

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

  private readonly language = inject(APP_LANGUAGE);

  protected readonly text = computed(() => notificationText(this.notification(), this.language));

  protected readonly category = computed(() => categoryWord(this.notification().category));

  /** "Warning, unread:" for a screen reader, ahead of the title. */
  protected readonly severityLabel = computed(() => {
    const word = severityWord(this.notification().severity);

    return this.unread()
      ? $localize`:@@notifications.severity.unread:${word}:severity:, unread:`
      : $localize`:@@notifications.severity.read:${word}:severity::`;
  });

  protected readonly absolute = computed(() =>
    formatDate(this.language, this.notification().createdAt, 'dateTime'),
  );

  protected readonly relative = computed(() =>
    this.timeStyle() === 'time'
      ? formatDate(this.language, this.notification().createdAt, 'time')
      : relativeTime(this.notification().createdAt, Date.now(), this.language),
  );
}

function severityWord(severity: NotificationSeverity): string {
  switch (severity) {
    case 'Critical':
      return $localize`:@@notifications.severity.critical:Critical`;
    case 'Warning':
      return $localize`:@@notifications.severity.warning:Warning`;
    default:
      return $localize`:@@notifications.severity.information:Information`;
  }
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
export function relativeTime(
  iso: string,
  now: number = Date.now(),
  language: AppLanguage = 'en',
): string {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);

  if (Math.abs(seconds) < 45) {
    return $localize`:@@time.justNow:just now`;
  }

  const minutes = Math.round(seconds / 60);

  if (Math.abs(minutes) < 60) {
    return formatRelative(language, minutes, 'minute');
  }

  const hours = Math.round(minutes / 60);

  if (Math.abs(hours) < 24) {
    return formatRelative(language, hours, 'hour');
  }

  const days = Math.round(hours / 24);

  if (Math.abs(days) < 7) {
    return formatRelative(language, days, 'day');
  }

  return formatDate(language, iso, 'date');
}
