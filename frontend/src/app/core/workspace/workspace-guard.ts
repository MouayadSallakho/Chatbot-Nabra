import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { WorkspaceContextService } from './workspace-context-service';

/** Where an unresolved workspace sends the operator. */
export const WORKSPACE_SETUP_PATH = 'workspace-setup';

/**
 * Keeps the admin application out of reach until a real workspace is loaded.
 *
 * <strong>This is not an authorisation guard.</strong> It answers "is a tenant
 * selected and does it exist", never "may this person see it" — there is no
 * person, no session and no permission model. It exists so that screens which
 * need a tenant id are never rendered without one, which is a correctness
 * concern rather than a security one.
 *
 * Resolution happens once. After the first successful load the workspace is
 * already in memory, so navigating between admin pages costs no extra request.
 */
export const workspaceGuard: CanActivateFn = async (): Promise<boolean | UrlTree> => {
  const workspace = inject(WorkspaceContextService);
  const router = inject(Router);

  if (workspace.isReady()) {
    return true;
  }

  const status = await workspace.resolve();

  // 'absent' and 'unavailable' both land on the setup screen, which tells the
  // two apart and offers the right repair for each: create a workspace, or
  // retry the one that already exists.
  return status === 'ready' || router.createUrlTree(['/', WORKSPACE_SETUP_PATH]);
};
