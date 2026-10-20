import type { Routes } from '@angular/router';
import { warmRoute } from './route-warmup';

/**
 * Warming a deep link's code during startup (Phase 36.11).
 *
 * What matters: exactly the chunks the router will ask for on this path are
 * started -- the shell's child, its lazy routes, the parameterised leaf -- and
 * nothing else; anything it cannot match ends quietly.
 */
describe('warmRoute', () => {
  const flush = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));
  const component = () => vi.fn(() => Promise.resolve(class {}));

  function config() {
    const signIn = component();
    const inbox = component();
    const placeholder = component();
    const detail = component();
    const overview = component();
    const children: Routes = [
      { path: '', loadComponent: placeholder },
      { path: ':conversationId', loadComponent: detail },
    ];
    const lazy = vi.fn(() => Promise.resolve([{ path: '', loadComponent: inbox, children }]));
    const routes: Routes = [
      { path: 'sign-in', loadComponent: signIn },
      {
        path: '',
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          { path: 'overview', loadComponent: overview },
          { path: 'conversations', loadChildren: lazy },
        ],
      },
    ];

    return { routes, signIn, inbox, placeholder, detail, overview, lazy };
  }

  it('starts the inbox and the conversation for a deep link, and nothing else', async () => {
    const c = config();

    warmRoute(c.routes, '/conversations/01a15013?x=1');
    await flush();

    expect(c.lazy).toHaveBeenCalledTimes(1);
    expect(c.inbox).toHaveBeenCalledTimes(1);
    expect(c.detail).toHaveBeenCalledTimes(1);
    expect(c.placeholder).not.toHaveBeenCalled();
    expect(c.overview).not.toHaveBeenCalled();
    expect(c.signIn).not.toHaveBeenCalled();
  });

  it('starts the empty child for the bare list', async () => {
    const c = config();

    warmRoute(c.routes, '/conversations');
    await flush();

    expect(c.inbox).toHaveBeenCalledTimes(1);
    expect(c.placeholder).toHaveBeenCalledTimes(1);
    expect(c.detail).not.toHaveBeenCalled();
  });

  it('follows a plain route inside the shell', async () => {
    const c = config();

    warmRoute(c.routes, '/overview');
    await flush();

    expect(c.overview).toHaveBeenCalledTimes(1);
    expect(c.lazy).not.toHaveBeenCalled();
  });

  it('does nothing for a path it cannot match, and never throws on a failed import', async () => {
    const c = config();
    const broken: Routes = [
      { path: 'reports', loadChildren: () => Promise.reject(new Error('gone')) },
    ];

    expect(() => warmRoute(c.routes, '/no-such-page')).not.toThrow();
    expect(() => warmRoute(broken, '/reports')).not.toThrow();
    await flush();

    expect(c.lazy).not.toHaveBeenCalled();
    expect(c.overview).not.toHaveBeenCalled();
  });
});
