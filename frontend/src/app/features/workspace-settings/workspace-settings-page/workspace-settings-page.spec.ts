import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { ToastService } from '@app/core/feedback/toast-service';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_PROBLEM_WORDS } from '@app/features/workspace-setup/workspace-problems';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WorkspaceSettingsPage } from './workspace-settings-page';

/**
 * The workspace settings page (Phase 38.4, D1).
 *
 * What matters: every save carries `If-Match` and keeps the tag it gets back; a
 * stale save keeps what was typed; a member without `workspace.manage` reads the
 * name and is told who can change it; and refusals are worded by code.
 */
describe('WorkspaceSettingsPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const tenantUrl = `/api/tenants/${tenantId}`;
  const renameUrl = `${tenantUrl}/name`;

  const acme = { id: tenantId, name: 'Acme Trading', slug: 'acme', status: 'Active' as const };

  let fixture: ComponentFixture<WorkspaceSettingsPage>;
  let http: HttpTestingController;

  async function mount(...granted: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [WorkspaceSettingsPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: PermissionApi, useValue: permissionApiGranting(...granted) },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt(acme);

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WorkspaceSettingsPage);
    fixture.detectChanges();
  }

  /** Mounts a member and answers the page's one read. */
  async function open(...granted: readonly string[]): Promise<void> {
    await mount('workspace.view', ...granted);
    http.expectOne(tenantUrl).flush(acme, { headers: { ETag: '"v1"' } });
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';
  const field = (): HTMLInputElement =>
    host().querySelector<HTMLInputElement>('[data-testid="workspace-name-input"]')!;
  const save = (): HTMLButtonElement =>
    host().querySelector<HTMLButtonElement>('[data-testid="save-name"]')!;

  function type(value: string): void {
    field().value = value;
    field().dispatchEvent(new Event('input'));
  }

  function submit(): void {
    host().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  const toasts = (): string[] =>
    TestBed.inject(ToastService)
      .toasts()
      .map((toast) => toast.text);

  afterEach(() => http.verify());

  it('shows the name in a field for somebody who may change it, and who sees it', async () => {
    await open('workspace.manage');

    expect(field().value).toBe('Acme Trading');
    expect(field().getAttribute('dir')).toBe('auto');
    expect(text()).toContain('Visitors see this name on the chat page and the widget.');
    // Nothing to save yet, and the button stays focusable rather than disabled.
    expect(save().disabled).toBe(false);
    expect(save().getAttribute('aria-disabled')).toBe('true');
  });

  it('saves with the version it read, and keeps the version it gets back', async () => {
    await open('workspace.manage');

    type('  Acme Holdings  ');
    await settle();
    submit();
    await settle();

    const first = http.expectOne(renameUrl);

    expect(first.request.method).toBe('PUT');
    expect(first.request.headers.get('If-Match')).toBe('"v1"');
    // Sent trimmed, as the server would store it.
    expect(first.request.body).toEqual({ name: 'Acme Holdings' });

    first.flush({ ...acme, name: 'Acme Holdings' }, { headers: { ETag: '"v2"' } });
    await settle();

    expect(toasts()).toContain('The workspace is now called Acme Holdings.');
    // The top bar and the workspace menu read the list in memory.
    expect(TestBed.inject(WorkspaceContextService).tenant()?.name).toBe('Acme Holdings');
    expect(field().value).toBe('Acme Holdings');

    type('Acme Group');
    await settle();
    submit();
    await settle();

    const second = http.expectOne(renameUrl);

    expect(second.request.headers.get('If-Match')).toBe('"v2"');
    second.flush({ ...acme, name: 'Acme Group' }, { headers: { ETag: '"v3"' } });
    await settle();
  });

  it('ignores a press with nothing to save', async () => {
    await open('workspace.manage');

    save().click();
    submit();
    await settle();

    // verify() in afterEach proves no request was sent.
    expect(text()).not.toContain('Unsaved changes');
  });

  it('keeps the typed name when the workspace was renamed elsewhere, and never retries', async () => {
    await open('workspace.manage');

    type('Acme Holdings');
    await settle();
    submit();
    await settle();

    http
      .expectOne(renameUrl)
      .flush(
        { title: 'Precondition Failed', code: 'precondition.failed' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await settle();

    expect(host().querySelector('[data-testid="settings-stale"]')).not.toBeNull();
    expect(text()).toContain('This workspace was renamed somewhere else.');
    expect(field().value).toBe('Acme Holdings');
    // verify() proves there was no second PUT.
  });

  it('reloads the latest version on request, keeping what was typed beside the saved name', async () => {
    await open('workspace.manage');

    type('Acme Holdings');
    await settle();
    submit();
    await settle();
    http.expectOne(renameUrl).flush('stale', { status: 412, statusText: 'Precondition Failed' });
    await settle();

    host().querySelector<HTMLButtonElement>('[data-testid="settings-reload"]')!.click();
    await settle();

    http
      .expectOne(tenantUrl)
      .flush({ ...acme, name: 'Acme Partners' }, { headers: { ETag: '"v7"' } });
    await settle();

    expect(host().querySelector('[data-testid="settings-stale"]')).toBeNull();
    expect(field().value).toBe('Acme Holdings');
    expect(host().querySelector('[data-testid="saved-name"]')!.textContent).toContain(
      'Acme Partners',
    );
    expect(TestBed.inject(WorkspaceContextService).tenant()?.name).toBe('Acme Partners');

    submit();
    await settle();

    const retried = http.expectOne(renameUrl);

    expect(retried.request.headers.get('If-Match')).toBe('"v7"');
    retried.flush({ ...acme, name: 'Acme Holdings' }, { headers: { ETag: '"v8"' } });
    await settle();
  });

  it('treats a missing precondition as stale rather than showing it to a person', async () => {
    await open('workspace.manage');

    type('Acme Holdings');
    await settle();
    submit();
    await settle();

    http
      .expectOne(renameUrl)
      .flush(
        { title: 'Precondition Required', code: 'precondition.required' },
        { status: 428, statusText: 'Precondition Required' },
      );
    await settle();

    expect(host().querySelector('[data-testid="settings-stale"]')).not.toBeNull();
    expect(host().querySelector('[data-testid="settings-failure"]')).toBeNull();
  });

  it('words workspace.name.invalid by its code, never the server sentence', async () => {
    registerProblemWords(WORKSPACE_PROBLEM_WORDS);
    await open('workspace.manage');

    type('Acme Holdings');
    await settle();
    submit();
    await settle();

    http.expectOne(renameUrl).flush(
      {
        title: 'Bad Request',
        detail: 'The English sentence from the server.',
        code: 'workspace.name.invalid',
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(host().querySelector('[data-testid="settings-failure"]')!.textContent).toContain(
      'Enter a workspace name of up to 200 characters, on one line.',
    );
    expect(text()).not.toContain('The English sentence');
    expect(field().value).toBe('Acme Holdings');
  });

  it('says a name it already knows the server would refuse, without sending it', async () => {
    await open('workspace.manage');

    type('   ');
    await settle();
    submit();
    await settle();

    expect(host().querySelector('#workspace-name-error')!.textContent).toContain(
      'Enter a workspace name of up to 200 characters, on one line.',
    );
    expect(field().getAttribute('aria-invalid')).toBe('true');

    type('x'.repeat(201));
    await settle();
    submit();
    await settle();

    expect(host().querySelector('#workspace-name-error')).not.toBeNull();
    // verify() proves nothing was sent.
  });

  it('reports a refusal of the save without losing the typed name', async () => {
    await open('workspace.manage');

    type('Acme Holdings');
    await settle();
    submit();
    await settle();

    http.expectOne(renameUrl).flush('no', { status: 403, statusText: 'Forbidden' });
    await settle();

    expect(host().querySelector('[data-testid="settings-failure"]')!.textContent).toContain(
      'You do not have permission to do this.',
    );
    expect(field().value).toBe('Acme Holdings');
  });

  it('counts a typed name as unsaved for the leave-page guard', async () => {
    await open('workspace.manage');

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

    type('Acme Holdings');
    await settle();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    expect(text()).toContain('Unsaved changes');
    expect(host().querySelector('[data-testid="saved-name"]')!.textContent).toContain(
      'Acme Trading',
    );
  });

  it('shows the name as text to a member who cannot manage the workspace, and says who can', async () => {
    await open();

    expect(host().querySelector('[data-testid="workspace-name-input"]')).toBeNull();
    expect(host().querySelector('[data-testid="save-name"]')).toBeNull();
    expect(host().querySelector('[data-testid="workspace-name-text"]')!.textContent).toContain(
      'Acme Trading',
    );
    expect(host().querySelector('[data-testid="settings-read-only"]')!.textContent).toContain(
      'Only people who can manage this workspace can change these.',
    );
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('explains a missing permission without asking the server', async () => {
    await mount();
    await settle();

    expect(host().querySelector('[data-testid="settings-denied"]')).not.toBeNull();
    expect(text()).not.toContain('403');
  });

  it('shows the shape of the card while loading', async () => {
    await mount('workspace.view');

    expect(host().querySelector('[aria-busy="true"] .skeleton-title')).not.toBeNull();
    expect(text()).toContain('Loading the workspace settings');

    http.expectOne(tenantUrl).flush(acme, { headers: { ETag: '"v1"' } });
    await settle();
  });

  it('reports a failed read with a way to try again', async () => {
    await mount('workspace.view');

    http.expectOne(tenantUrl).flush('nope', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('Could not load the workspace settings');

    [...host().querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => (button.textContent ?? '').includes('Try again'))!
      .click();
    await settle();

    http.expectOne(tenantUrl).flush(acme, { headers: { ETag: '"v1"' } });
    await settle();

    expect(text()).toContain('Acme Trading');
  });
});
