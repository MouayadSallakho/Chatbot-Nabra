import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { EmailPreferences, NotificationApi } from '@app/core/notifications/notification-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { PageHeader } from '@app/shared/page-header/page-header';
import { NotificationsNav } from '../notifications-nav';

/** One category, as the member reads it. */
interface Category {
  readonly key: keyof EmailPreferences;
  readonly label: string;
  readonly description: string;
}

/**
 * The member's own operational-email choices.
 *
 * <strong>Email is off until turned on.</strong> In-app notifications are always
 * kept; this only decides which of them are also emailed. There is no digest,
 * no quiet hours and no recipient list — five categories, each on or off.
 *
 * <strong>Saved against the version read.</strong> A change made in another tab
 * since this one loaded is refused, and the page offers to reload rather than
 * silently overwriting it.
 */
@Component({
  selector: 'app-notification-preferences',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, PageHeader, NotificationsNav],
  templateUrl: './notification-preferences.html',
  styleUrl: './notification-preferences.scss',
})
export class NotificationPreferencesPage {
  private readonly api = inject(NotificationApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly categories: readonly Category[] = [
    {
      key: 'reports',
      label: 'Reports',
      description: 'A report you requested, or one of your schedules, finished or failed.',
    },
    {
      key: 'usage',
      label: 'Usage',
      description: 'AI usage reached 70%, 90% or 100% of the monthly limit.',
    },
    {
      key: 'billing',
      label: 'Billing',
      description: 'The subscription became past due, was suspended, ended or recovered.',
    },
    {
      key: 'knowledge',
      label: 'Knowledge',
      description: 'Indexing a knowledge document failed.',
    },
    {
      key: 'channelHealth',
      label: 'Channel health',
      description: 'A WhatsApp delivery outcome could not be confirmed.',
    },
  ];

  protected readonly form = new FormGroup({
    reports: new FormControl(false, { nonNullable: true }),
    usage: new FormControl(false, { nonNullable: true }),
    billing: new FormControl(false, { nonNullable: true }),
    knowledge: new FormControl(false, { nonNullable: true }),
    channelHealth: new FormControl(false, { nonNullable: true }),
  });

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly emailConfigured = signal(true);
  protected readonly announcement = signal('');
  private etag: string | null = null;

  constructor() {
    void this.load();
  }

  protected async save(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null || this.etag === null || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);

    try {
      const saved = await firstValueFrom(
        this.api.savePreferences(tenantId, this.form.getRawValue(), this.etag),
      );

      this.etag = saved.etag;
      this.form.reset(saved.value.email);
      this.announcement.set('Email preferences saved.');
      // Save is disabled while saving and again once nothing has changed, so the
      // browser has already dropped focus on the body. Back to the start of the
      // choices, so the reader is still in the form they were using.
      afterNextRender(
        () => this.host.nativeElement.querySelector<HTMLElement>('.category-switch')?.focus(),
        { injector: this.injector },
      );
    } catch (error: unknown) {
      this.saveFailure.set(toApiError(error));
    } finally {
      this.saving.set(false);
    }
  }

  /** Discards this tab's choices and starts again from what is stored now. */
  protected async reload(): Promise<void> {
    this.saveFailure.set(null);
    await this.load();
  }

  private async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (tenantId === null) {
      return;
    }

    this.loading.set(true);

    try {
      const stored = await firstValueFrom(this.api.preferences(tenantId));

      this.etag = stored.etag;
      this.emailConfigured.set(stored.value.emailConfigured);
      this.form.reset(stored.value.email);
      this.failure.set(null);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
