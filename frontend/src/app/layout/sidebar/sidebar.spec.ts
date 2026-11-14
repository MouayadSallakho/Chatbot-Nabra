import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Tenant } from '@app/core/api/tenant-api';
import { PermissionService } from '@app/core/authorization/permission-service';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { NavCounts } from '@app/layout/nav-counts';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { Sidebar } from './sidebar';

const NORTHWIND: Tenant = { id: 'n-1', name: 'Northwind Operations', slug: 'nw', status: 'Active' };
const AL_NOOR: Tenant = {
  id: 'a-2',
  name: 'شركة النور للتجارة الإلكترونية',
  slug: 'alnoor',
  status: 'Active',
};

/** Every permission the owner holds (the side bar only asks `hasAny` and `has`). */
const OWNER = ['*'];
/** The demo analyst: workspace, analytics and reports, nothing else (36.1). */
const ANALYST = ['workspace.view', 'analytics.view', 'reports.view', 'reports.generate'];

/**
 * The floating side bar (Phase 39.3): its sections and areas per permission, the
 * inbox's count, the rail, the switcher and the member.
 *
 * Assertions are on semantics -- names, roles, `aria-current`, what is in the
 * accessibility tree -- never on pixels.
 */
describe('Sidebar', () => {
  let fixture: ComponentFixture<Sidebar>;
  let needsPerson: ReturnType<typeof signal<number | null>>;
  let shown: number;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const groupLabels = (): string[] =>
    [...root().querySelectorAll('.nav-group-label')].map((el) => el.textContent!.trim());
  const links = (): HTMLAnchorElement[] => [
    ...root().querySelectorAll<HTMLAnchorElement>('a.nav-item'),
  ];
  const linkNames = (): string[] =>
    links().map((link) => link.querySelector('.nav-label')!.textContent!.trim());

  async function render(
    options: {
      granted?: readonly string[];
      collapsed?: boolean;
      workspaces?: readonly Tenant[];
      displayName?: string | null;
    } = {},
  ) {
    const granted = options.granted ?? OWNER;
    const allows = (code: string) => granted.includes('*') || granted.includes(code);

    needsPerson = signal<number | null>(null);
    shown = 0;

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
        {
          provide: PermissionService,
          useValue: {
            hasAny: (...codes: string[]) => codes.some(allows),
            has: allows,
          },
        },
        {
          provide: NavCounts,
          useValue: {
            needsPerson,
            show: () => {
              shown += 1;

              return () => (shown -= 1);
            },
          },
        },
        {
          provide: NotificationCenter,
          useValue: {
            unread: signal(0),
            unreadLabel: signal('No unread notifications'),
            recent: signal([]),
            loading: signal(false),
            failure: signal(null),
            loadRecent: async () => undefined,
            markRead: async () => undefined,
            markAllRead: async () => undefined,
          },
        },
      ],
    });

    const workspaces = options.workspaces ?? [NORTHWIND];

    fixture = TestBed.createComponent(Sidebar);
    fixture.componentRef.setInput('collapsed', options.collapsed ?? false);
    fixture.componentRef.setInput('workspaceName', workspaces[0].name);
    fixture.componentRef.setInput('workspaceId', workspaces[0].id);
    fixture.componentRef.setInput('workspaces', workspaces);
    fixture.componentRef.setInput('email', 'sara.ahmed@example.test');
    fixture.componentRef.setInput('displayName', options.displayName ?? null);
    document.body.appendChild(root());
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => root()?.remove());

  it('is the navigation landmark, named, with the product’s name', async () => {
    await render();

    const nav = root().querySelector('nav')!;

    expect(nav.getAttribute('aria-label')).toBe('Primary');
    expect(root().querySelector('.brand-name')?.textContent?.trim()).toBe('Nabra');
  });

  it('groups an owner’s areas under four short labels, in the side bar’s order', async () => {
    await render();

    expect(groupLabels()).toEqual(['Conversations', 'AI', 'Insights', 'Workspace']);
    expect(linkNames()).toEqual([
      'Overview',
      'Inbox',
      'Customers',
      'Knowledge',
      'AI Agents',
      'Channels',
      'Analytics',
      'Reports',
      'Team',
      'Roles & permissions',
      'Workspace settings',
      'Plan & usage',
      'Billing',
    ]);

    // Each list is named by its label, so a screen reader says which section it is in.
    const list = root().querySelector('ul')!;
    const label = root().querySelector(`#${list.getAttribute('aria-labelledby')}`);

    expect(label?.textContent?.trim()).toBe('Conversations');
  });

  it('shows a restricted member only what they may open, and drops an empty section', async () => {
    await render({ granted: ANALYST });

    expect(groupLabels()).toEqual(['Conversations', 'Insights', 'Workspace']);
    expect(linkNames()).toEqual([
      'Overview',
      'Analytics',
      'Reports',
      'Workspace settings',
      'Plan & usage',
    ]);
    // Nothing of the inbox: no count is asked for.
    expect(shown).toBe(0);
  });

  it('shows the inbox’s needs-a-person count, in words for a screen reader', async () => {
    await render();

    expect(shown).toBe(1);
    expect(root().querySelector('[data-testid="nav-inbox-count"]')).toBeNull();

    needsPerson.set(148);
    fixture.detectChanges();

    const inbox = root().querySelector<HTMLAnchorElement>('[data-testid="nav-conversations"]')!;

    expect(root().querySelector('[data-testid="nav-inbox-count"]')?.textContent?.trim()).toBe(
      '148',
    );
    expect(inbox.querySelector('.nav-label')?.textContent?.trim()).toBe('Inbox');
    expect(inbox.querySelector('.visually-hidden')?.textContent).toBe(', Needs a person: 148');
    expect(
      root().querySelector('[data-testid="nav-inbox-count"]')?.getAttribute('aria-hidden'),
    ).toBe('true');

    // Nobody waiting: no pill at all, rather than a "0".
    needsPerson.set(0);
    fixture.detectChanges();
    expect(root().querySelector('[data-testid="nav-inbox-count"]')).toBeNull();
  });

  it('caps a very large count so the pill stays one short word', async () => {
    await render();

    needsPerson.set(12_345);
    fixture.detectChanges();

    expect(root().querySelector('[data-testid="nav-inbox-count"]')?.textContent?.trim()).toBe(
      '999+',
    );
    // A screen reader hears the exact number.
    expect(
      root().querySelector('[data-testid="nav-conversations"] .visually-hidden')?.textContent,
    ).toBe(', Needs a person: 12,345');
  });

  it('stops showing the count when the side bar goes', async () => {
    await render();

    expect(shown).toBe(1);
    fixture.destroy();
    expect(shown).toBe(0);
  });

  describe('collapsed to the rail', () => {
    it('keeps every name in the accessibility tree, visually hidden', async () => {
      await render({ collapsed: true });

      expect(linkNames()).toContain('Inbox');
      for (const label of root().querySelectorAll('.nav-label')) {
        expect(label.classList).toContain('visually-hidden');
      }
      expect(root().querySelector('.brand-name')).toBeNull();
    });

    it('stands a thin divider where each later section’s label was', async () => {
      await render({ collapsed: true });

      expect(root().querySelectorAll('.nav-divider')).toHaveLength(3);
      for (const label of root().querySelectorAll('.nav-group-label')) {
        expect(label.classList).toContain('visually-hidden');
      }
    });

    it('shows an area’s name in a tooltip on focus, without changing the link’s name', async () => {
      await render({ collapsed: true });

      const knowledge = root().querySelector<HTMLAnchorElement>('[data-testid="nav-knowledge"]')!;

      knowledge.focus();
      knowledge.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
      fixture.detectChanges();

      const tip = knowledge.querySelector<HTMLElement>('[role="tooltip"]')!;

      expect(tip.hidden).toBe(false);
      expect(tip.textContent).toBe('Knowledge');
      expect(tip.getAttribute('aria-hidden')).toBe('true');
    });

    it('shows no tooltip on the full side bar, where the name is already on screen', async () => {
      await render();

      const knowledge = root().querySelector<HTMLAnchorElement>('[data-testid="nav-knowledge"]')!;

      knowledge.dispatchEvent(new MouseEvent('mouseenter'));
      fixture.detectChanges();

      expect(knowledge.querySelector<HTMLElement>('[role="tooltip"]')?.hidden).toBe(true);
    });

    it('puts the count on the inbox’s icon', async () => {
      await render({ collapsed: true });

      needsPerson.set(3);
      fixture.detectChanges();

      expect(root().querySelector('.nav-count.is-corner')?.textContent?.trim()).toBe('3');
    });
  });

  describe('the workspace', () => {
    it('is only a name when there is nowhere to switch to', async () => {
      await render();

      expect(root().querySelector('[data-testid="workspace-name"]')?.textContent).toContain(
        'Northwind Operations',
      );
      expect(root().querySelector('[data-testid="workspace-switch"]')).toBeNull();
    });

    it('opens a menu of the account’s workspaces and reports the one chosen', async () => {
      await render({ workspaces: [NORTHWIND, AL_NOOR] });

      const chosen: Tenant[] = [];

      fixture.componentInstance.workspaceChosen.subscribe((tenant) => chosen.push(tenant));
      root().querySelector<HTMLButtonElement>('[data-testid="workspace-switch"]')!.click();
      fixture.detectChanges();
      await fixture.whenStable();

      const options = [...root().querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')];

      expect(options.map((option) => option.getAttribute('aria-checked'))).toEqual([
        'true',
        'false',
      ]);
      // An Arabic name keeps its own direction inside the English interface.
      expect(options[1].querySelector('bdi')?.getAttribute('dir')).toBe('auto');

      options[1].click();
      expect(chosen).toEqual([AL_NOOR]);
    });
  });

  describe('the member', () => {
    it('shows the address and the sounds state when no name was chosen', async () => {
      await render();

      const trigger = root().querySelector<HTMLButtonElement>(
        'app-profile-menu [aria-haspopup="menu"]',
      )!;

      expect(trigger.textContent).toContain('sara.ahmed@example.test');
      expect(trigger.textContent).toContain('Sounds on');
      expect(trigger.textContent).toContain('Account menu');
      expect(trigger.querySelector('.avatar')?.textContent?.trim()).toBe('SA');
    });

    it('shows the display name, with initials from it', async () => {
      await render({ displayName: 'ليلى حداد' });

      const trigger = root().querySelector<HTMLButtonElement>(
        'app-profile-menu [aria-haspopup="menu"]',
      )!;

      expect(trigger.textContent).toContain('ليلى حداد');
      expect(trigger.querySelector('.avatar')?.textContent?.trim()).toBe('لح');
    });

    it('is the avatar alone on the rail, named in full', async () => {
      await render({ collapsed: true });

      const trigger = root().querySelector<HTMLButtonElement>(
        'app-profile-menu [aria-haspopup="menu"]',
      )!;

      expect(trigger.getAttribute('aria-label')).toBe(
        'SA: Account menu for sara.ahmed@example.test',
      );
    });
  });
});
