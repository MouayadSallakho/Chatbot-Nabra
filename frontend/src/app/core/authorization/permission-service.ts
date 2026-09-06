import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PermissionCode } from './permission-codes';
import { PermissionApi } from './permission-api';

/**
 * What the signed-in person may do in the workspace they are currently in.
 *
 * <strong>This is user experience, and nothing else.</strong> It exists so the
 * admin stops offering actions the server would refuse — a disabled button with
 * an explanation is a better answer than a `403` after a click. Anybody can edit
 * the JavaScript running in their own browser, so this class could be made to say
 * anything at all without a single extra byte becoming reachable: every request
 * is authorised again on the server, against a membership row and the roles the
 * member holds.
 *
 * <strong>In memory only.</strong> Nothing here is written to `localStorage`,
 * `sessionStorage` or a cookie. Permissions are not credentials, so there is
 * nothing to protect — but a persisted copy would be a stale copy, and a client
 * showing a button because of what was true last week is worse than one that
 * asks. A reload re-asks the server, which is the only thing that can answer.
 *
 * <strong>Scoped to one workspace at a time.</strong> Authority is per tenant, so
 * switching workspace discards what was held and asks again. Keeping a map of
 * every workspace's permissions would be a cache with no invalidation story.
 */
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private readonly api = inject(PermissionApi);

  private readonly held = signal<ReadonlySet<string>>(new Set());
  private readonly loadedFor = signal<string | null>(null);

  /**
   * Whether the answer for the current workspace has arrived.
   *
   * Screens use this to avoid flashing a read-only state at somebody who does
   * have permission: before it is true, this application does not know, and it
   * must not guess in either direction.
   */
  readonly resolved = computed(() => this.loadedFor() !== null);

  /** The workspace these permissions are about, or null before the first load. */
  readonly tenantId = this.loadedFor.asReadonly();

  /**
   * Every permission the person holds here, for the screens that render a
   * summary rather than branch on one code.
   */
  readonly all = computed<readonly string[]>(() => [...this.held()]);

  /**
   * Loads the caller's permissions for a workspace.
   *
   * <strong>A failure grants nothing.</strong> If the request fails the held set
   * is emptied and resolution is still recorded, so the admin renders as though
   * the person may do nothing rather than as though they may do everything. That
   * is the only safe direction: the alternative would briefly offer every action
   * on a flaky connection, and each one would come back refused.
   *
   * A `401` is handled by the session interceptor, which signs the reader out; a
   * `404` means they do not belong to the workspace, which the workspace guard
   * resolves. Neither is reported here, because neither has a useful thing to say
   * on this screen.
   */
  async load(tenantId: string): Promise<void> {
    try {
      const codes = await firstValueFrom(this.api.mine(tenantId));

      this.held.set(new Set(codes));
    } catch {
      this.held.set(new Set());
    } finally {
      this.loadedFor.set(tenantId);
    }
  }

  /**
   * Forgets everything.
   *
   * Called on sign-out and when the workspace changes. The permissions belonged
   * to one account in one workspace, and showing the next person what the last
   * one could do would be both wrong and confusing.
   */
  clear(): void {
    this.held.set(new Set());
    this.loadedFor.set(null);
  }

  /**
   * Whether the person holds a capability.
   *
   * <strong>The one method screens should use.</strong> Comparing strings inline
   * in twenty templates is how a typo becomes a permanently hidden feature and
   * how a renamed code gets missed in nineteen places — hence the typed union
   * rather than a bare string.
   */
  has(code: PermissionCode): boolean {
    return this.held().has(code);
  }

  /** Whether the person holds any of these capabilities. */
  hasAny(...codes: readonly PermissionCode[]): boolean {
    return codes.some((code) => this.held().has(code));
  }

  /**
   * Whether the person could grant this capability to a role.
   *
   * <strong>The delegation ceiling, mirrored for the interface only.</strong>
   * The server refuses to let anybody hand out authority they do not hold, so
   * offering a checkbox for a capability the reader lacks would be offering an
   * action certain to come back refused. Owner holds the whole catalogue, so this
   * filters nothing away from them without needing to know what an Owner is.
   *
   * It is the same question as {@link has}, deliberately named separately: these
   * are the same set today and mean different things, and a later rule about
   * delegation should have somewhere to live that is not every call site of
   * `has`.
   */
  canDelegate(code: string): boolean {
    return this.held().has(code);
  }

  /**
   * Whether every capability in a set is one the person could grant.
   *
   * Used to decide whether a role is theirs to administer at all: a role
   * carrying authority the reader does not hold is not editable by them, and the
   * screen should say so rather than present controls the server will refuse.
   */
  canDelegateAll(codes: readonly string[]): boolean {
    return codes.every((code) => this.held().has(code));
  }

  /**
   * A signal that tracks one capability.
   *
   * For templates and computed values, which need to re-evaluate when the held
   * set changes. `has()` reads the signal too, so it is reactive inside a
   * template expression; this exists for the cases that want to pass the answer
   * around as a value.
   */
  can(code: PermissionCode): () => boolean {
    return computed(() => this.held().has(code));
  }
}
