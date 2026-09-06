import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { WorkspaceContextService } from './workspace-context-service';

/** Where a reader with no selected workspace is sent. */
export const WORKSPACE_SETUP_PATH = 'workspace';

/**
 * Keeps workspace-scoped screens out of reach until one is selected.
 *
 * <strong>Still not an authorization guard, and now it is not even the closest
 * thing to one.</strong> It answers "is a workspace selected", never "may this
 * person see it" — that question is answered on the server, against a membership
 * row, on every single request. This exists so that screens which need a tenant
 * id are never rendered without one, which is a correctness concern.
 *
 * It runs after the authentication guard, so by the time it is reached there is
 * a signed-in account whose workspaces can be asked for. The list is loaded once
 * at startup; this only reloads it if something cleared it, which is why moving
 * between admin pages costs no request.
 */
export const workspaceGuard: CanActivateFn = async (): Promise<boolean | UrlTree> => {
  const workspace = inject(WorkspaceContextService);
  const router = inject(Router);

  if (workspace.isReady()) {
    return true;
  }

  await workspace.load();

  // 'none' and 'unavailable' both land on the workspace screen, which tells the
  // two apart and offers the right repair for each: create or choose a
  // workspace, or retry a backend that did not answer.
  return workspace.isReady() || router.createUrlTree(['/', WORKSPACE_SETUP_PATH]);
};
