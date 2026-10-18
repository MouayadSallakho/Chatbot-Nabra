import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { Tenant } from '@app/core/api/tenant-api';
import { AuthService } from '@app/core/auth/auth-service';
import {
  WORKSPACE_STORAGE_KEY,
  WorkspaceContextService,
} from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { WorkspaceSetup } from './workspace-setup';

/**
 * Choosing or creating the workspace to work in.
 *
 * The screen a new account lands on, and the one somebody returns to when they
 * belong to more than one workspace.
 */
describe('WorkspaceSetup', () => {
  const acme: Tenant = {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Acme Trading',
    slug: 'acme-trading',
    status: 'Active',
  };

  const rival: Tenant = {
    id: '22222222-2222-2222-2222-222222222222',
    name: 'Rival Trading',
    slug: 'rival-trading',
    status: 'Active',
  };

  let fixture: ComponentFixture<WorkspaceSetup>;
  let http: HttpTestingController;
  let storage: Storage;
  let workspace: WorkspaceContextService;
  let navigatedTo: unknown[] | null;

  function mount(seed: Record<string, string> = {}): void {
    navigatedTo = null;
    storage = memoryStorage(seed);

    TestBed.configureTestingModule({
      imports: [WorkspaceSetup],
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    const router = TestBed.inject(Router);

    router.navigate = ((url: unknown[]) => {
      navigatedTo = url;

      return Promise.resolve(true);
    }) as Router['navigate'];

    http = TestBed.inject(HttpTestingController);
    workspace = TestBed.inject(WorkspaceContextService);
  }

  /** Loads the given workspaces into the context, then renders the screen. */
  async function render(workspaces: readonly Tenant[]): Promise<void> {
    const loading = workspace.load();

    http.expectOne('/api/me/tenants').flush(workspaces);
    await loading;

    fixture = TestBed.createComponent(WorkspaceSetup);
    fixture.detectChanges();
    await settle();
  }

  /** Renders the screen after the workspace list failed to load. */
  async function renderUnreachable(): Promise<void> {
    const loading = workspace.load();

    http.expectOne('/api/me/tenants').error(new ProgressEvent('error'), { status: 0 });
    await loading;

    fixture = TestBed.createComponent(WorkspaceSetup);
    fixture.detectChanges();
    await settle();
  }

  /**
   * Renders, then lets every pending continuation run.
   *
   * The extra yield matters here and not in the single-request screens. This
   * application is zoneless, so `whenStable` reports that change detection is
   * idle -- it does not wait for promises. Signing in makes two requests in
   * sequence, and without a turn of the event loop between them the second has
   * not been issued yet when the test looks for it.
   */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const nameBox = () => host().querySelector<HTMLInputElement>('#workspace-name')!;
  const slugBox = () => host().querySelector<HTMLInputElement>('#workspace-slug')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  const buttonSaying = (text: string) =>
    [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes(text),
    );

  async function typeName(value: string): Promise<void> {
    nameBox().value = value;
    nameBox().dispatchEvent(new Event('input'));
    await settle();
  }

  afterEach(() => http.verify());

  describe('when the account has no workspaces', () => {
    beforeEach(async () => {
      mount();
      await render([]);
    });

    it('offers to create one', () => {
      expect(host().textContent).toContain('Create your workspace');
      expect(nameBox()).not.toBeNull();
      expect(slugBox()).not.toBeNull();
    });

    it('says the creator becomes a member', () => {
      // The reason creating a workspace is immediately usable, stated where
      // somebody can read it.
      expect(host().textContent).toContain('You will be its first member');
    });

    it('asks for no credential', () => {
      // The account already exists by the time this screen is reached. A
      // password field here would be a second sign-in.
      const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

      expect(inputs.map((input) => input.id)).toEqual(['workspace-name', 'workspace-slug']);
      expect(host().querySelector('input[type="password"]')).toBeNull();
    });

    it('suggests a slug from the name, and lets it be overridden', async () => {
      await typeName('Acme Trading Co.');

      expect(slugBox().value).toBe('acme-trading-co');

      slugBox().value = 'something-else';
      slugBox().dispatchEvent(new Event('input'));
      await settle();
      await typeName('Acme Trading Co. Renamed');

      expect(slugBox().value).toBe('something-else');
    });

    it('refuses to submit an incomplete form', async () => {
      submit().click();
      await settle();

      expect(host().textContent).toContain('A workspace name is required.');
      http.verify();
    });

    it('creates a real workspace and enters it', async () => {
      await typeName('Acme Trading');

      submit().click();
      await settle();

      const request = http.expectOne('/api/tenants');

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Acme Trading', slug: 'acme-trading' });

      request.flush(acme);
      await settle();

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(acme.id);
      expect(workspace.tenant()).toEqual(acme);
      expect(navigatedTo).toEqual(['/', 'overview']);
    });

    it('reports a rejected slug in the backend words and stays put', async () => {
      await typeName('Acme Trading');

      submit().click();
      await settle();

      http
        .expectOne('/api/tenants')
        .flush(
          { title: 'Tenant slug already exists', detail: "The slug 'acme-trading' is taken." },
          { status: 409, statusText: 'Conflict' },
        );
      await settle();

      expect(host().textContent).toContain("The slug 'acme-trading' is taken.");
      expect(navigatedTo).toBeNull();
    });

    it('explains an unreachable backend without blaming the form', async () => {
      await typeName('Acme Trading');

      submit().click();
      await settle();

      http.expectOne('/api/tenants').error(new ProgressEvent('error'), { status: 0 });
      await settle();

      expect(host().textContent).toContain('could not be reached');
    });
  });

  describe('when the account belongs to several workspaces', () => {
    beforeEach(async () => {
      mount();
      await render([acme, rival]);
    });

    it('offers the choice rather than picking one', () => {
      expect(host().textContent).toContain('Choose a workspace');
      expect(host().textContent).toContain('Acme Trading');
      expect(host().textContent).toContain('Rival Trading');
    });

    it('does not show the create form by default', () => {
      // The common case is "pick one and get on with it". Burying that under a
      // form would be the wrong emphasis.
      expect(host().querySelector('#workspace-name')).toBeNull();
    });

    it('enters the chosen workspace', async () => {
      buttonSaying('Rival Trading')!.click();
      await settle();

      expect(workspace.tenant()).toEqual(rival);
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(rival.id);
      expect(navigatedTo).toEqual(['/', 'overview']);
    });

    it('offers to create another, behind a deliberate action', async () => {
      buttonSaying('Create another workspace')!.click();
      await settle();

      expect(nameBox()).not.toBeNull();

      buttonSaying('Cancel')!.click();
      await settle();

      expect(host().querySelector('#workspace-name')).toBeNull();
    });
  });

  describe('when only the workspaces the account belongs to are offered', () => {
    it('lists nothing the server did not return', async () => {
      // The central rule: a hand-edited id in localStorage cannot make a
      // workspace appear here, because the list is the server's answer.
      mount({ [WORKSPACE_STORAGE_KEY]: rival.id });
      await render([acme]);

      // One workspace, so it was selected outright and the chooser is not shown.
      expect(host().textContent).not.toContain('Rival Trading');
      expect(workspace.tenant()).toEqual(acme);
    });

    it('shows only the returned workspaces even when storage names another', async () => {
      mount({ [WORKSPACE_STORAGE_KEY]: '99999999-9999-9999-9999-999999999999' });
      await render([acme, rival]);

      const options = [...host().querySelectorAll('.workspace-option')];

      expect(options).toHaveLength(2);
      expect(host().textContent).not.toContain('99999999');
    });
  });

  describe('when the workspace list cannot be loaded', () => {
    beforeEach(async () => {
      mount({ [WORKSPACE_STORAGE_KEY]: acme.id });
      await renderUnreachable();
    });

    it('reports a connection problem rather than an empty account', () => {
      expect(host().textContent).toContain('Nabra is not responding');
      expect(host().textContent).toContain('Nothing has been changed');
    });

    it('offers no create form, so nobody makes a duplicate to fix a restart', () => {
      expect(host().querySelector('#workspace-name')).toBeNull();
    });

    it('keeps the remembered selection', () => {
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(acme.id);
    });

    it('retries on request', async () => {
      buttonSaying('Try again')!.click();
      await settle();

      http.expectOne('/api/me/tenants').flush([acme]);
      await settle();

      expect(navigatedTo).toEqual(['/', 'overview']);
    });
  });

  describe('the signed-in account', () => {
    it('is named, with a way to sign out', async () => {
      mount();

      TestBed.inject(AuthService);
      await render([]);

      // The account is unresolved in this harness, so only the control is
      // asserted -- its presence is the point: somebody who landed here on the
      // wrong account needs a way out that is not "clear your browser storage".
      expect(buttonSaying('Sign out')).toBeDefined();
    });

    it('ends the session and returns to sign-in', async () => {
      mount();
      await render([]);

      buttonSaying('Sign out')!.click();
      await settle();

      http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expect(navigatedTo).toEqual(['/', 'sign-in']);
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    });
  });

  describe('the welcome (Phase 36.5)', () => {
    it('gives a new account a direction after its first workspace', async () => {
      mount();
      await render([]);

      expect(host().textContent).toContain('Next, you will set up an AI agent');
      expect(slugBox().getAttribute('dir')).toBe('ltr');
    });

    it('marks each workspace with its first letter and keeps the slug left to right', async () => {
      mount();
      await render([acme, rival]);

      const options = [...host().querySelectorAll('.workspace-option')];

      expect(options.map((option) => option.querySelector('.workspace-tile')?.textContent)).toEqual(
        ['A', 'R'],
      );
      expect(options[0].querySelector('.workspace-tile')?.getAttribute('aria-hidden')).toBe('true');
      expect(options[0].querySelector('.workspace-name')?.getAttribute('dir')).toBe('auto');
      expect(options[0].querySelector('.workspace-slug bdi')?.getAttribute('dir')).toBe('ltr');
    });

    it('does not repeat the first-workspace direction when creating another', async () => {
      mount();
      await render([acme, rival]);

      buttonSaying('Create another workspace')!.click();
      await settle();

      expect(host().textContent).not.toContain('Next, you will set up an AI agent');
    });
  });
});
