import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import {
  WORKSPACE_STORAGE_KEY,
  WorkspaceContextService,
} from '@app/core/workspace/workspace-context-service';
import { memoryStorage } from '@app/testing/test-doubles';
import { WorkspaceSetup } from './workspace-setup';

/**
 * The first-run screen.
 *
 * As well as the create flow, these assert what the screen must never become:
 * a sign-in. It says on its face that there is no authentication, and nothing on
 * it asks for a credential.
 */
describe('WorkspaceSetup', () => {
  const tenant = {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Acme Trading',
    slug: 'acme-trading',
    status: 'Active' as const,
  };

  let fixture: ComponentFixture<WorkspaceSetup>;
  let http: HttpTestingController;
  let storage: Storage;
  let navigatedTo: unknown[] | null;

  function mount(seed: Record<string, string> = {}): void {
    navigatedTo = null;
    storage = memoryStorage(seed);

    TestBed.configureTestingModule({
      imports: [WorkspaceSetup],
      providers: [
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
    fixture = TestBed.createComponent(WorkspaceSetup);
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const nameBox = () => host().querySelector<HTMLInputElement>('#workspace-name')!;
  const slugBox = () => host().querySelector<HTMLInputElement>('#workspace-slug')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  async function typeName(value: string): Promise<void> {
    nameBox().value = value;
    nameBox().dispatchEvent(new Event('input'));
    await settle();
  }

  afterEach(() => http.verify());

  describe('when no workspace is selected', () => {
    it('offers to create one', () => {
      mount();

      expect(host().textContent).toContain('Create a development workspace');
      expect(nameBox()).not.toBeNull();
      expect(slugBox()).not.toBeNull();
    });

    it('says plainly that it is not a sign-in', () => {
      // A reviewer looking at a polished first-run flow would otherwise
      // reasonably assume this is authentication.
      mount();

      expect(host().textContent).toContain('not a sign-in');
      expect(host().textContent).toContain('no accounts');
    });

    it('asks for no credential of any kind', () => {
      // Asserted on the controls, not the prose: the disclaimer legitimately
      // uses the word "passwords" to say there are none.
      mount();

      const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

      expect(inputs.map((input) => input.id)).toEqual(['workspace-name', 'workspace-slug']);
      expect(inputs.every((input) => input.type === 'text')).toBe(true);
      expect(host().querySelector('input[type="password"]')).toBeNull();
      expect(host().querySelector('[autocomplete*="password"]')).toBeNull();
    });

    it('suggests a slug from the name, and lets it be overridden', async () => {
      mount();
      await typeName('Acme Trading Co.');

      expect(slugBox().value).toBe('acme-trading-co');

      slugBox().value = 'something-else';
      slugBox().dispatchEvent(new Event('input'));
      await settle();
      await typeName('Acme Trading Co. Renamed');

      // Once edited by hand it is left alone.
      expect(slugBox().value).toBe('something-else');
    });

    it('refuses to submit an incomplete form', async () => {
      mount();

      submit().click();
      await settle();

      expect(host().textContent).toContain('A workspace name is required.');
      http.verify();
    });

    it('creates a real tenant, stores its id and enters the application', async () => {
      mount();
      await typeName('Acme Trading');

      submit().click();
      await settle();

      const request = http.expectOne('/api/tenants');

      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Acme Trading', slug: 'acme-trading' });

      request.flush(tenant);
      await settle();

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(tenant.id);
      expect(TestBed.inject(WorkspaceContextService).tenant()).toEqual(tenant);
      expect(navigatedTo).toEqual(['/', 'overview']);
    });

    it('reports a rejected slug in the backend words and stays put', async () => {
      mount();
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
      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
    });

    it('explains an unreachable backend without blaming the form', async () => {
      mount();
      await typeName('Acme Trading');

      submit().click();
      await settle();

      http.expectOne('/api/tenants').error(new ProgressEvent('error'), { status: 0 });
      await settle();

      expect(host().textContent).toContain('Could not reach');
    });
  });

  describe('when the stored workspace cannot be reached', () => {
    async function unreachable(): Promise<void> {
      mount({ [WORKSPACE_STORAGE_KEY]: tenant.id });

      const workspace = TestBed.inject(WorkspaceContextService);
      const resolving = workspace.resolve();

      http.expectOne(`/api/tenants/${tenant.id}`).error(new ProgressEvent('error'), { status: 0 });
      await resolving;
      await settle();
    }

    it('reports a connection problem rather than a missing workspace', async () => {
      await unreachable();

      expect(host().textContent).toContain('Cannot reach the backend');
      expect(host().textContent).toContain('has not been changed or cleared');
    });

    it('offers no create form, so nobody makes a duplicate to fix a restart', async () => {
      await unreachable();

      expect(host().querySelector('#workspace-name')).toBeNull();
    });

    it('keeps the stored workspace', async () => {
      await unreachable();

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBe(tenant.id);
    });

    it('retries the stored workspace on request', async () => {
      await unreachable();

      const retry = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        button.textContent?.includes('Try again'),
      );

      retry!.click();
      await settle();

      http.expectOne(`/api/tenants/${tenant.id}`).flush(tenant);
      await settle();

      expect(navigatedTo).toEqual(['/', 'overview']);
    });

    it('lets the operator deliberately forget it', async () => {
      await unreachable();

      const forget = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        button.textContent?.includes('Forget this workspace'),
      );

      forget!.click();
      await settle();

      expect(storage.getItem(WORKSPACE_STORAGE_KEY)).toBeNull();
      // And the create form comes back.
      expect(host().querySelector('#workspace-name')).not.toBeNull();
    });
  });
});
