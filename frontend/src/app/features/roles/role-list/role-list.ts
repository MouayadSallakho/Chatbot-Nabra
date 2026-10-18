import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { RoleApi, RoleSummary } from '@app/core/authorization/role-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { LocalDatePipe } from '@app/core/i18n/locale-pipes';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * The workspace's roles.
 *
 * <strong>Fetched fresh on every visit.</strong> There is no client cache: the
 * list is small, the request is cheap, and a cache would have to be invalidated
 * by every edit on the role screens. Refetching is both simpler and more
 * truthful -- what is on screen is what the server has.
 *
 * The Owner row is present but reads differently: it is marked as protected, it
 * reports the full permission count, and it opens a read-only detail rather than
 * an editor. Hiding it would be worse -- somebody looking for "who can do
 * everything here" would find nothing and reasonably conclude nobody can.
 */
@Component({
  selector: 'app-role-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon, LocalDatePipe],
  templateUrl: './role-list.html',
  styleUrl: './role-list.scss',
})
export class RoleList {
  private readonly api = inject(RoleApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly roles = signal<readonly RoleSummary[]>([]);

  /** Placeholder rows, so the loading state has the shape of the real one. */
  protected readonly skeletonRows = [0, 1];

  /** Whether the reader may see this screen's contents at all. */
  protected readonly canView = computed(() => this.permissions.has('roles.view'));

  /**
   * Whether to offer creating a role.
   *
   * Both permissions, and deliberately so: creating one means choosing from the
   * catalogue, and the catalogue is published only to `roles.view`. Offering the
   * action to somebody who could not load the form would be a dead end.
   */
  protected readonly canCreate = computed(
    () => this.permissions.has('roles.manage') && this.permissions.has('roles.view'),
  );

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canView()) {
      // Nothing to ask for. The template explains the refusal rather than
      // showing an error from a request that was never worth making.
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);

    try {
      this.roles.set(await firstValueFrom(this.api.list(tenantId)));
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
