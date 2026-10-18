import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { Customer, CustomerRow, CustomersApi } from '../customers-api';

/**
 * One customer: who they are, how they reach this workspace, and what they have
 * talked about.
 *
 * <strong>Two operations, and the second is irreversible.</strong> Setting a
 * label is an ordinary conditional edit. Combining two records moves one
 * person's channel identities and whole conversation history onto another and
 * cannot be undone in this release, so it is behind an explicit confirmation
 * that names both records and says what it does.
 *
 * <strong>Nothing here suggests who might be the same person.</strong> The
 * target is chosen from a bounded list the reader pages through, not from a
 * ranked list of guesses. A suggestion would be the platform making an identity
 * claim it cannot support, and people accept suggestions.
 *
 * <strong>Every value is escaped.</strong> A label is untrusted text and a
 * masked reference comes from a provider. Both are interpolated; there is no
 * `innerHTML` on this page.
 */
@Component({
  selector: 'app-customer-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState, RouterLink, FormsModule],
  templateUrl: './customer-detail.html',
  styleUrl: './customer-detail.scss',
})
export class CustomerDetail implements OnInit {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(CustomersApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** How many candidates the merge picker offers at a time. */
  private static readonly CandidatePageSize = 20;

  /** From the route, through `withComponentInputBinding`. */
  readonly customerId = input.required<string>();

  protected readonly canView = computed(() => this.permissions.has('customers.view'));

  /** Whether this is the reader's to change. Viewing does not imply naming. */
  protected readonly canManage = computed(() => this.permissions.has('customers.manage'));

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly customer = signal<Customer | null>(null);
  protected readonly version = signal('');

  /** The label being edited, which survives a failed save. */
  protected readonly draft = signal('');
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly saveFailure = signal<string | null>(null);

  /** Whether the merge step is open, and what it is aimed at. */
  protected readonly merging = signal(false);
  protected readonly candidates = signal<readonly CustomerRow[]>([]);
  protected readonly target = signal<CustomerRow | null>(null);
  protected readonly mergeFailure = signal<string | null>(null);
  protected readonly mergeRunning = signal(false);

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  /** True when this record has been combined into another. */
  protected readonly merged = computed(() => this.customer()?.mergedIntoCustomerId ?? null);

  /** Whether the label in the box differs from the one on the server. */
  protected readonly dirty = computed(
    () => this.draft().trim() !== (this.customer()?.displayName ?? ''),
  );

  /**
   * Loads on init rather than in the constructor.
   *
   * The route binds `customerId` after construction, so a constructor that read
   * it would be reading an input nobody had set yet.
   */
  ngOnInit(): void {
    void this.load();
  }

  protected async reload(): Promise<void> {
    await this.load();
  }

  /** Saves the label, carrying the version the reader's copy was read at. */
  protected async save(): Promise<void> {
    const tenantId = this.tenantId();
    const customer = this.customer();

    if (tenantId === '' || customer === null || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.saved.set(false);

    try {
      const next = await firstValueFrom(
        this.api.rename(
          tenantId,
          customer.id,
          this.draft().trim() === '' ? null : this.draft().trim(),
          this.version(),
        ),
      );

      this.apply(next.customer, next.version);
      this.saved.set(true);
    } catch (error: unknown) {
      // The draft is deliberately left alone. Somebody who lost a race has not
      // stopped wanting what they typed, and clearing the box would make them
      // type it again.
      this.saveFailure.set(this.describe(toApiError(error)));
    } finally {
      this.saving.set(false);
    }
  }

  /** Opens the merge step and loads a bounded list of candidates. */
  protected async openMerge(): Promise<void> {
    this.merging.set(true);
    this.mergeFailure.set(null);
    this.target.set(null);

    const tenantId = this.tenantId();

    if (tenantId === '') {
      return;
    }

    try {
      const page = await firstValueFrom(
        this.api.list(tenantId, 1, CustomerDetail.CandidatePageSize),
      );

      // Never this customer, and never a record that is already a tombstone: the
      // directory excludes merged records, so the only one to remove is this one.
      this.candidates.set(page.items.filter((row) => row.id !== this.customerId()));
      // The button that opened the picker has been replaced by it.
      this.focusAfterRender('.candidate input');
    } catch (error: unknown) {
      this.mergeFailure.set(this.describe(toApiError(error)));
    }
  }

  protected closeMerge(): void {
    this.merging.set(false);
    this.target.set(null);
    this.mergeFailure.set(null);
    this.focusAfterRender('[data-testid="open-merge"]');
  }

  protected choose(candidate: CustomerRow): void {
    this.target.set(candidate);
  }

  /** Performs the merge, after the reader has confirmed it in words. */
  protected async confirmMerge(): Promise<void> {
    const tenantId = this.tenantId();
    const target = this.target();

    if (tenantId === '' || target === null || this.mergeRunning()) {
      return;
    }

    this.mergeRunning.set(true);
    this.mergeFailure.set(null);

    try {
      await firstValueFrom(this.api.merge(tenantId, this.customerId(), target.id, this.version()));

      this.closeMerge();

      await this.load();
      // This page is now the tombstone; the notice saying where the history went
      // is what the reader needs next.
      this.focusAfterRender('[data-testid="merged-notice"]');
    } catch (error: unknown) {
      this.mergeFailure.set(this.describe(toApiError(error)));
      // The confirm button was disabled while it ran, which dropped focus.
      this.focusAfterRender('[data-testid="merge-failure"]');
    } finally {
      this.mergeRunning.set(false);
    }
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }

  protected date(value: string): string {
    return this.locale.date(value, 'dateTime');
  }

  protected channelLabel(channel: string): string {
    return channel === 'WebChat' ? 'Website chat' : channel;
  }

  protected conversations(count: number): string {
    return count === 1 ? '1 conversation' : `${count} conversations`;
  }

  /**
   * What tells one candidate apart from another.
   *
   * <strong>Found in the browser.</strong> Two unnamed website visitors both
   * render as "Anonymous visitor", so the merge picker offered a choice between
   * two identical rows for an operation that cannot be undone. The channels
   * somebody used and when the workspace first heard from them are the only
   * distinguishing facts the platform actually holds, so they are the ones shown
   * — never a guess, and never anything taken from what was said.
   *
   * Found again in Phase 33.5: visitors who arrived on the same day still read
   * identically, so "first seen" is to the minute and every row ends with a
   * short reference from the record's own identifier -- the same one in its
   * address, and the only fact guaranteed to differ between two records.
   */
  protected distinguish(row: CustomerRow): string {
    return this.summarise(row.id, row.channels, row.createdAt);
  }

  /** The same description, for the customer whose page this is. */
  protected distinguishThis(person: Customer): string {
    return this.summarise(person.id, person.channels, person.createdAt);
  }

  private summarise(id: string, channels: readonly string[], createdAt: string): string {
    const where =
      channels.length === 0
        ? 'No channel'
        : channels.map((channel) => this.channelLabel(channel)).join(', ');

    return `${where} · first seen ${this.date(createdAt)} · ref. ${id.slice(-6)}`;
  }

  /**
   * A refusal in the reader's terms.
   *
   * The two that matter are a lost race and a record that has already been
   * combined. Both are ordinary things rather than errors, and both have a next
   * step, so both say what it is.
   */
  private describe(error: ApiError): string {
    if (error.kind === 'stale') {
      return 'Somebody else changed this customer while you were editing. Reload the page and apply your change again — nothing you typed has been lost.';
    }

    if (error.kind === 'conflict') {
      return 'This customer has already been combined with another one. Reload the page to see where their history went.';
    }

    if (error.kind === 'preconditionRequired') {
      return 'This page is out of date. Reload it and try again.';
    }

    return error.message || 'That could not be saved.';
  }

  private apply(customer: Customer, version: string): void {
    this.customer.set(customer);
    this.version.set(version);
    this.draft.set(customer.displayName ?? '');
  }

  private async load(): Promise<void> {
    if (!this.canView()) {
      this.loading.set(false);

      return;
    }

    const tenantId = this.tenantId();

    if (tenantId === '') {
      this.loading.set(false);

      return;
    }

    try {
      const found = await firstValueFrom(this.api.get(tenantId, this.customerId()));

      this.apply(found.customer, found.version);
      this.failure.set(null);
      this.saveFailure.set(null);
      this.saved.set(false);
    } catch (error: unknown) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
