import type { Route, Routes } from '@angular/router';

/**
 * Starts downloading the code a path will need while startup is still busy
 * with the session (Phase 36.11).
 *
 * The router decides nothing until the app initializer has finished: the
 * session, the workspace and its permissions. Only then did it begin fetching
 * the lazy chunks of a deep-linked page, so those downloads queued behind four
 * requests that had nothing to do with them. This walks the same route
 * configuration the router will, for the same path, and calls the same
 * `loadChildren` and `loadComponent` functions. The bundler keeps each module's
 * promise, so when the router asks a moment later the code is already there or
 * on its way. Nothing is rendered, guarded or navigated here: what the reader
 * may see is still decided by the router and the server.
 *
 * Plain segments and `:parameters` only, the only kinds this application
 * uses; anything else (a redirect, a matcher, a wildcard) ends the walk. A
 * failed import is ignored -- the router will report it in its own time.
 */
export function warmRoute(routes: Routes, path: string): void {
  const segments = path.split(/[?#]/)[0].split('/').filter(Boolean);

  void walk(routes, segments).catch(() => undefined);
}

async function walk(routes: Routes, segments: readonly string[]): Promise<void> {
  for (const route of routes) {
    const consumed = match(route, segments);

    if (consumed === null) {
      continue;
    }

    const rest = segments.slice(consumed);

    // A pathless route only groups its children: try it, and if nothing under
    // it matches the rest, the next sibling may.
    if (consumed === 0 && rest.length > 0 && !(await hasMatch(route, rest))) {
      continue;
    }

    const component: unknown = route.loadComponent?.();

    if (component instanceof Promise) {
      component.catch(() => undefined);
    }

    const children = route.children ?? (await loadChildren(route));

    if (children) {
      await walk(children, rest);
    }

    return;
  }
}

/** How many segments a route consumes, or null when it does not match. */
function match(route: Route, segments: readonly string[]): number | null {
  if (route.redirectTo !== undefined || route.matcher || route.path === '**') {
    return null;
  }

  const parts = (route.path ?? '').split('/').filter(Boolean);

  if (parts.length > segments.length) {
    return null;
  }

  return parts.every((part, index) => part.startsWith(':') || part === segments[index])
    ? parts.length
    : null;
}

async function hasMatch(route: Route, segments: readonly string[]): Promise<boolean> {
  const children = route.children ?? (await loadChildren(route));

  return (children ?? []).some((child) => match(child, segments) !== null);
}

async function loadChildren(route: Route): Promise<Routes | null> {
  if (!route.loadChildren) {
    return null;
  }

  const loaded: unknown = await route.loadChildren();

  if (Array.isArray(loaded)) {
    return loaded as Routes;
  }

  const module = loaded as { default?: unknown } | null;

  return Array.isArray(module?.default) ? (module.default as Routes) : null;
}
