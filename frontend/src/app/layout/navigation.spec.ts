import {
  INBOX_PATH,
  NAV_GROUPS,
  NAV_ITEMS,
  navItemForUrl,
  phoneBarSplit,
  visibleNavItems,
} from './navigation';

const paths = (items: readonly { path: string }[]) => items.map((item) => item.path);

/** The navigation model behind the side bar and the phone's bars (Phase 39.3). */
describe('navigation', () => {
  it('keeps each section’s entries together, in the sections’ order', () => {
    const order = NAV_ITEMS.map((item) => NAV_GROUPS.indexOf(item.group));

    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(NAV_GROUPS).toEqual(['Conversations', 'AI', 'Insights', 'Workspace']);
  });

  it('calls the conversations area the inbox, as the glossary does', () => {
    expect(NAV_ITEMS.find((item) => item.path === INBOX_PATH)?.label).toBe('Inbox');
  });

  it('finds the area a URL belongs to, ignoring the query and the fragment', () => {
    expect(navItemForUrl('/conversations/abc?view=needs-person')?.path).toBe('conversations');
    expect(navItemForUrl('/channels/website-chat#snippet')?.path).toBe('channels');
    expect(navItemForUrl('/notifications')).toBeNull();
    expect(navItemForUrl('/')).toBeNull();
  });

  describe('the phone’s bottom bar', () => {
    const everything = visibleNavItems(() => true);

    it('takes the inbox, the overview, the customers and the knowledge first', () => {
      const { bar, more } = phoneBarSplit(everything);

      expect(paths(bar)).toEqual(['conversations', 'overview', 'customers', 'knowledge']);
      expect(paths(more)).toEqual([
        'ai-agents',
        'channels',
        'analytics',
        'reports',
        'team',
        'roles',
        'workspace-settings',
        'plan-usage',
        'billing',
      ]);
    });

    it('stands analytics and reports in for areas the member may not open', () => {
      const analyst = visibleNavItems((...codes) =>
        codes.some((code) => ['workspace.view', 'analytics.view', 'reports.view'].includes(code)),
      );
      const { bar, more } = phoneBarSplit(analyst);

      expect(paths(bar)).toEqual(['overview', 'analytics', 'reports']);
      expect(paths(more)).toEqual(['workspace-settings', 'plan-usage']);
    });

    it('never puts an area in both places, and loses none', () => {
      const { bar, more } = phoneBarSplit(everything);

      expect([...paths(bar), ...paths(more)].sort()).toEqual(paths(everything).sort());
    });
  });
});
