import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { WorkspaceSubscription } from '../billing-api';
import { daysLeft, grantsPlan, intervalWord, subscriptionStatus } from '../billing-words';

/**
 * What the workspace's subscription says (Phase 36.10).
 *
 * The status as a chip (a word with a glyph of its own) and one sentence; while a
 * payment has failed, the grace deadline in words ("Plan kept until 27
 * October, 5 days left"); whenever the fallback applies, which plan is in
 * effect now beside the one subscribed to; a scheduled cancellation said
 * outright; then the dates.
 *
 * <strong>Presentational.</strong> It draws what the server returned and calls
 * nothing, so the billing page shows the real subscription and the Design Lab
 * can show every state with labelled sample data.
 */
@Component({
  selector: 'app-subscription-summary',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'subscription', '[attr.data-status]': 'subscription().status' },
  template: `
    <div class="subscription-head">
      <p class="subscription-plan" dir="auto">{{ subscription().planName }}</p>
      <span class="chip" [class]="status().chip" data-testid="status">{{ status().word }}</span>
    </div>
    <p class="subscription-sentence" data-testid="status-sentence">{{ status().sentence }}</p>

    @if (grace(); as deadline) {
      <div class="subscription-callout is-warning" data-testid="grace">
        <p class="subscription-callout-title">
          <span i18n="@@billing.grace.until">Plan kept until</span>{{ ' '
          }}<time [attr.datetime]="subscription().graceEndsAt">{{ deadline.date }}</time
          ><span class="subscription-days"> · {{ deadline.left }}</span>
        </p>
        <p i18n="@@billing.grace.body">
          After that the workspace moves to a smaller plan unless a payment goes through. Nothing is
          deleted.
        </p>
      </div>
    }

    @if (fallback()) {
      <dl class="admin-facts facts subscription-plans" data-testid="fallback">
        <div>
          <dt i18n="@@billing.plan.inEffect">In effect now</dt>
          <dd dir="auto" data-testid="plan-in-effect">{{ currentPlanName() }}</dd>
        </div>
        <div>
          <dt i18n="@@billing.plan.subscribed">Subscribed plan</dt>
          <dd>
            <span dir="auto">{{ subscription().planName }}</span>
            <span class="subscription-not-in-effect" i18n="@@billing.plan.notInEffect"
              >(not in effect)</span
            >
          </dd>
        </div>
      </dl>
    }

    @if (subscription().cancelAtPeriodEnd && subscription().status !== 'Canceled') {
      <!--
        Said outright: the chip still reads Active, correctly, and a reader
        scanning it would otherwise not know it will not renew.
      -->
      <p
        class="subscription-callout is-warning"
        data-testid="cancellation-scheduled"
        i18n="@@billing.cancellation"
      >
        Cancellation is scheduled. This subscription will not renew, and the workspace keeps its
        plan until the date below.
      </p>
    }

    <dl class="admin-facts facts">
      <div>
        <dt i18n="@@billing.facts.period">Billing period</dt>
        <dd>{{ interval() }}</dd>
      </div>
      @if (subscription().trialEndsAt; as trial) {
        <div>
          <dt i18n="@@billing.facts.trialEnds">Trial ends</dt>
          <dd data-testid="trial-ends">{{ date(trial) }}</dd>
        </div>
      }
      @if (periodEnd(); as end) {
        <div>
          <dt>
            @if (subscription().cancelAtPeriodEnd) {
              <span i18n="@@billing.facts.accessEnds">Access ends</span>
            } @else {
              <span i18n="@@billing.facts.renews">Renews</span>
            }
          </dt>
          <dd data-testid="period-end">{{ date(end) }}</dd>
        </div>
      }
    </dl>
  `,
})
export class SubscriptionSummary {
  private readonly locale = inject(LocaleFormat);

  readonly subscription = input.required<WorkspaceSubscription>();

  /** The plan the workspace is on now (the server's `currentPlanName`). */
  readonly currentPlanName = input.required<string>();

  protected readonly status = computed(() => subscriptionStatus(this.subscription().status));
  protected readonly interval = computed(() => intervalWord(this.subscription().interval));

  /** Whether the fallback plan applies rather than the subscribed one. */
  protected readonly fallback = computed(
    () =>
      !grantsPlan(this.subscription().status) &&
      this.currentPlanName() !== this.subscription().planName,
  );

  /**
   * When the period ends, while the subscription keeps its plan: a renewal (or,
   * once cancelled, the end of access). Suspended, ended and unpaid ones do not
   * renew, so the date would mislead.
   */
  protected readonly periodEnd = computed(() =>
    grantsPlan(this.subscription().status) ? this.subscription().currentPeriodEnd : null,
  );

  protected readonly grace = computed(() => {
    const ends = this.subscription().graceEndsAt;

    if (!ends || this.subscription().status !== 'PastDue') {
      return null;
    }

    return {
      date: this.date(ends),
      left: daysLeft(ends, Date.now(), this.locale.language),
    };
  });

  protected date(value: string): string {
    return this.locale.date(value, 'longDate');
  }
}
