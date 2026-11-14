import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { PermissionService } from '@app/core/authorization/permission-service';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { NotificationCenter } from '@app/core/notifications/notification-center';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { Topbar } from './topbar';

/** The phone's glass top bar (Phase 39.3): the area, the workspace, the actions. */
describe('Topbar', () => {
  let fixture: ComponentFixture<Topbar>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;

  async function render(areaTitle: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
        { provide: PermissionService, useValue: { hasAny: () => true, has: () => true } },
        {
          provide: NotificationCenter,
          useValue: {
            unread: signal(3),
            unreadLabel: signal('3 unread'),
            recent: signal([]),
            loading: signal(false),
            failure: signal(null),
            loadRecent: async () => undefined,
          },
        },
      ],
    });

    fixture = TestBed.createComponent(Topbar);
    fixture.componentRef.setInput('areaTitle', areaTitle);
    fixture.componentRef.setInput('workspaceName', 'Northwind Operations');
    fixture.componentRef.setInput('workspaceId', 'n-1');
    fixture.componentRef.setInput('workspaces', [
      { id: 'n-1', name: 'Northwind Operations', slug: 'nw', status: 'Active' },
    ]);
    fixture.componentRef.setInput('email', 'ada.owner@example.test');
    document.body.appendChild(root());
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => root()?.remove());

  it('is the banner, with the area as context rather than a second heading', async () => {
    await render('Inbox');

    expect(root().querySelector('[role="banner"]')).not.toBeNull();
    expect(root().querySelector('[data-testid="area-title"]')?.tagName).toBe('P');
    expect(root().querySelector('[data-testid="area-title"]')?.textContent?.trim()).toBe('Inbox');
    expect(root().querySelector('h1, h2')).toBeNull();
  });

  it('names its icon-only controls', async () => {
    await render('Inbox');

    expect(
      root().querySelector('[data-testid="palette-trigger"]')?.getAttribute('aria-label'),
    ).toBe('Search or jump to…');
    expect(
      root().querySelector('[data-testid="palette-trigger"]')?.getAttribute('aria-keyshortcuts'),
    ).toBe('Control+K');
    expect(
      root().querySelector('[data-testid="notification-bell"]')?.getAttribute('aria-label'),
    ).toBe('Notifications, 3 unread');
    expect(
      root().querySelector('app-profile-menu [aria-haspopup="menu"]')?.getAttribute('aria-label'),
    ).toBe('AO: Account menu for ada.owner@example.test');
  });

  it('shows the workspace alone where there is no area', async () => {
    await render('');

    expect(root().querySelector('[data-testid="area-title"]')).toBeNull();
    expect(root().querySelector('[data-testid="workspace-name"]')?.textContent).toContain(
      'Northwind Operations',
    );
  });

  it('asks for the palette', async () => {
    await render('Inbox');

    let asked = 0;

    fixture.componentInstance.paletteRequested.subscribe(() => (asked += 1));
    root().querySelector<HTMLButtonElement>('[data-testid="palette-trigger"]')!.click();

    expect(asked).toBe(1);
  });
});
