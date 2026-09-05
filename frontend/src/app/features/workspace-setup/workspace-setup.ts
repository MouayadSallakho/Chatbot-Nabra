import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TenantApi } from '@app/core/api/tenant-api';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { HOME_PATH } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';

/** Mirrors the Domain's own tenant rules, for immediate feedback only. */
const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const SLUG_MIN = 3;
const SLUG_MAX = 63;
const NAME_MAX = 200;

/**
 * The first screen, when this browser has no workspace yet.
 *
 * <strong>It creates a real tenant through the real API.</strong> Nothing is
 * fabricated: the id, the status and the canonical name and slug all come back
 * from the backend, and the operator cannot choose any of them beyond the name
 * and slug they type.
 *
 * <strong>This is not a sign-in screen and must not become one.</strong> There
 * is no account, no password and no session — it is a development-only way to
 * point the admin UI at a tenant so the product screens can be built against
 * real data before authentication exists. The page says so on screen, because a
 * reviewer who sees a polished first-run flow will otherwise reasonably assume
 * it is one.
 *
 * It also handles the other reason there is no workspace: the backend could not
 * be reached. That case shows a connection error and a retry, and deliberately
 * does <em>not</em> offer to create a second workspace — the stored one is
 * probably fine, and inviting a duplicate would be the wrong repair.
 */
@Component({
  selector: 'app-workspace-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Icon],
  templateUrl: './workspace-setup.html',
  styleUrl: './workspace-setup.scss',
})
export class WorkspaceSetup {
  private readonly tenants = inject(TenantApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly router = inject(Router);

  protected readonly status = this.workspace.status;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly retrying = signal(false);

  protected readonly slugLimits = { min: SLUG_MIN, max: SLUG_MAX };

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(NAME_MAX)],
    }),
    slug: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.minLength(SLUG_MIN),
        Validators.maxLength(SLUG_MAX),
        Validators.pattern(SLUG_PATTERN),
      ],
    }),
  });

  /** Whether the stored workspace exists but could not be loaded. */
  protected readonly unreachable = computed(() => this.status() === 'unavailable');

  /**
   * Whether a field's error should be on screen yet.
   *
   * Only after the reader has left the field or tried to submit. Marking a
   * required field invalid while somebody is still typing the first character of
   * it is technically accurate and reads as nagging.
   */
  protected showError(field: 'name' | 'slug'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
  }

  private readonly submitAttempted = signal(false);

  /**
   * Fills the slug from the name, until the operator edits the slug themselves.
   *
   * A convenience, not a rule: the field stays editable and the backend remains
   * the authority on what a slug may be.
   */
  protected suggestSlug(): void {
    const slug = this.form.controls.slug;

    if (slug.dirty) {
      return;
    }

    slug.setValue(
      this.form.controls.name.value
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '')
        .slice(0, SLUG_MAX),
    );
  }

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    // The guard against a double submit as well as an invalid one. Without it a
    // fast second click creates a second workspace.
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const tenant = await firstValueFrom(
        this.tenants.create({
          name: this.form.controls.name.value.trim(),
          slug: this.form.controls.slug.value.trim(),
        }),
      );

      this.workspace.adopt(tenant);
      await this.router.navigate(['/', HOME_PATH]);
    } catch (error) {
      // The backend stays the authority. A rejected name or a slug somebody else
      // already holds is reported in its own words rather than re-derived here.
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  /** Tries the stored workspace again, after a connection failure. */
  protected async retry(): Promise<void> {
    this.retrying.set(true);

    try {
      if ((await this.workspace.resolve()) === 'ready') {
        await this.router.navigate(['/', HOME_PATH]);
      }
    } finally {
      this.retrying.set(false);
    }
  }

  /**
   * Abandons the stored workspace after a connection failure.
   *
   * Offered only in the unreachable state, and only as a deliberate choice: the
   * operator may know the tenant is gone even though the backend cannot
   * currently say so.
   */
  protected forget(): void {
    this.workspace.clear();
  }
}
