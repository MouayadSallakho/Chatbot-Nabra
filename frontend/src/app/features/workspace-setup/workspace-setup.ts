import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Tenant, TenantApi } from '@app/core/api/tenant-api';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { LOGIN_PATH } from '@app/core/auth/auth-guard';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { HOME_PATH } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';
import { EntryFrame } from '@app/layout/entry-frame/entry-frame';
import { WORKSPACE_PROBLEM_WORDS } from './workspace-problems';

/** Mirrors the Domain's own tenant rules, for immediate feedback only. */
const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const SLUG_MIN = 3;
const SLUG_MAX = 63;
const NAME_MAX = 200;

/**
 * Choosing or creating the workspace to work in.
 *
 * <strong>Every workspace offered here came from the server.</strong> The list is
 * the signed-in account's own memberships, read from `/api/me/tenants`; nothing
 * on this screen can select a tenant that is not in it. That is not the security
 * boundary — the server checks membership on every request regardless — but it
 * means no screen is ever built on an id this application invented.
 *
 * Three situations, and they are deliberately distinct:
 *
 * - No workspaces: offer to create one. This is where a new account lands.
 * - Several workspaces: offer the choice. One workspace is not a choice and is
 *   selected automatically, so this only appears when it is real.
 * - The list could not be loaded: say so, and offer a retry. Emphatically do
 *   <em>not</em> offer the create form here, which would invite somebody to make
 *   a duplicate workspace to repair a backend that is simply not running.
 */
@Component({
  selector: 'app-workspace-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EntryFrame, ReactiveFormsModule, Icon],
  templateUrl: './workspace-setup.html',
  styleUrl: './workspace-setup.scss',
})
export class WorkspaceSetup {
  private readonly tenants = inject(TenantApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly status = this.workspace.status;
  protected readonly workspaces = this.workspace.workspaces;
  protected readonly user = this.auth.user;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly retrying = signal(false);
  protected readonly creating = signal(false);

  protected readonly slugHint = $localize`:@@workspaceSetup.slug.hint:Lowercase letters, numbers and hyphens. Between ${SLUG_MIN}:min: and ${SLUG_MAX}:max: characters.`;

  private readonly submitAttempted = signal(false);

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

  constructor() {
    registerProblemWords(WORKSPACE_PROBLEM_WORDS);
  }

  /** Whether the account's workspaces could not be loaded at all. */
  protected readonly unreachable = computed(() => this.status() === 'unavailable');

  /** Each workspace with the first letter of its name, for the tile beside it. */
  protected readonly options = computed(() =>
    this.workspaces().map((workspace) => ({
      workspace,
      initial: Array.from(workspace.name.trim())[0]?.toLocaleUpperCase() ?? '',
    })),
  );

  /** Whether there is a choice of existing workspaces to present. */
  protected readonly hasWorkspaces = computed(() => this.workspaces().length > 0);

  /**
   * Whether the create form should be on screen.
   *
   * Always, for an account with no workspaces. For an account that has some, it
   * is behind a deliberate action, so the common case — pick one and get on with
   * it — is not buried under a form.
   */
  protected readonly showCreateForm = computed(() => !this.hasWorkspaces() || this.creating());

  protected showError(field: 'name' | 'slug'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
  }

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

  /** Opens the create form for an account that already has workspaces. */
  protected startCreating(): void {
    this.creating.set(true);
    this.failure.set(null);
  }

  /** Abandons the create form and goes back to the list. */
  protected cancelCreating(): void {
    this.creating.set(false);
    this.failure.set(null);
    this.form.reset();
    this.submitAttempted.set(false);
  }

  /** Enters one of the account's workspaces. */
  protected async choose(tenant: Tenant): Promise<void> {
    if (!(await this.workspace.select(tenant.id))) {
      // Only reachable if the list changed underneath the reader. Reloading is
      // the honest repair: it will either offer the workspace again or explain
      // that it is gone.
      await this.workspace.load();

      return;
    }

    await this.router.navigate(['/', HOME_PATH]);
  }

  protected async create(): Promise<void> {
    this.submitAttempted.set(true);

    // Guards against a double submit as well as an invalid one: without it a
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

      // The server made the creator a member in the same transaction, so this
      // workspace is immediately usable. Adopting it locally avoids a second
      // round trip to learn what we already know.
      await this.workspace.adopt(tenant);

      await this.router.navigate(['/', HOME_PATH]);
    } catch (error) {
      // The backend stays the authority. A rejected name or a slug somebody else
      // already holds is reported in its own words rather than re-derived here.
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  /** Tries the workspace list again, after a connection failure. */
  protected async retry(): Promise<void> {
    this.retrying.set(true);

    try {
      await this.workspace.load();

      if (this.workspace.isReady()) {
        await this.router.navigate(['/', HOME_PATH]);
      }
    } finally {
      this.retrying.set(false);
    }
  }

  /**
   * Signs out from this screen.
   *
   * Somebody who has landed here on the wrong account needs a way out that is
   * not "clear your browser storage". It is a real sign-out: the session ends on
   * the server.
   */
  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.workspace.clear();

    await this.router.navigate(['/', LOGIN_PATH]);
  }
}
