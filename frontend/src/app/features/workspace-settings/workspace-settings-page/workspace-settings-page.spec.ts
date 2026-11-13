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
import {
  answerConfirmation,
  memoryStorage,
  permissionApiGranting,
  stubDialogs,
} from '@app/testing/test-doubles';
import { WorkspaceSettingsPage, zoneGroups } from './workspace-settings-page';

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

  const zoneUrl = `${tenantUrl}/time-zone`;
  const schedulesUrl = `${tenantUrl}/reports/schedules`;

  /**
   * The read the page makes just before asking (BA-9): the count, under a newer
   * version on purpose, which the page must not adopt.
   */
  const countRead = (scheduleCount?: number) =>
    http
      .expectOne(tenantUrl)
      .flush(scheduleCount === undefined ? acme : { ...acme, scheduleCount }, {
        headers: { ETag: '"v9"' },
      });

  const acme = {
    id: tenantId,
    name: 'Acme Trading',
    slug: 'acme',
    status: 'Active' as const,
    timeZone: 'UTC',
  };

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
    expect(text()).toContain('This workspace was changed somewhere else.');
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

  describe('the time zone (E1)', () => {
    const select = (): HTMLSelectElement =>
      host().querySelector<HTMLSelectElement>('[data-testid="time-zone-select"]')!;
    const filter = (): HTMLInputElement =>
      host().querySelector<HTMLInputElement>('[data-testid="time-zone-filter"]')!;
    const saveZone = (): HTMLButtonElement =>
      host().querySelector<HTMLButtonElement>('[data-testid="save-zone"]')!;

    function choose(zone: string): void {
      select().value = zone;
      select().dispatchEvent(new Event('change'));
    }

    function submitZone(): void {
      host().querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    }

    it('offers UTC first, then every zone grouped by region, left to right', async () => {
      await open('workspace.manage');

      const options = [...select().options];
      const groups = [...select().querySelectorAll('optgroup')].map((group) => group.label);

      expect(options[0].value).toBe('UTC');
      expect(select().value).toBe('UTC');
      expect(select().getAttribute('dir')).toBe('ltr');
      expect(groups).toContain('Asia');
      expect(groups).toContain('Europe');
      expect(options.some((option) => option.value === 'Asia/Riyadh')).toBe(true);
      expect(text()).toContain(
        'Days in analytics, report periods and report schedules are counted in this zone.',
      );
    });

    it('shortens the list from a city or a region, and says when nothing matches', async () => {
      await open('workspace.manage');

      filter().value = 'new york';
      filter().dispatchEvent(new Event('input'));
      await settle();

      const values = [...select().options].map((option) => option.value);

      expect(values).toContain('America/New_York');
      expect(values).not.toContain('Asia/Riyadh');
      // UTC and the saved zone stay, so the field never silently changes.
      expect(values[0]).toBe('UTC');

      filter().value = 'atlantis';
      filter().dispatchEvent(new Event('input'));
      await settle();

      expect(text()).toContain('No time zone matches.');
    });

    it('saves without asking when no schedule would move, with the version it read', async () => {
      await open('workspace.manage', 'reports.view');

      choose('Asia/Riyadh');
      await settle();
      expect(text()).toContain('Unsaved changes');
      expect(host().querySelector('[data-testid="saved-zone"]')?.textContent).toContain('UTC');
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);

      submitZone();
      await settle();

      countRead(0);
      await settle();

      // No question, and never the schedule list (BA-9).
      expect(host().querySelector('dialog[open]')).toBeNull();
      http.expectNone(schedulesUrl);

      const put = http.expectOne(zoneUrl);

      expect(put.request.method).toBe('PUT');
      expect(put.request.headers.get('If-Match')).toBe('"v1"');
      expect(put.request.body).toEqual({ timeZone: 'Asia/Riyadh' });

      put.flush({ ...acme, timeZone: 'Asia/Riyadh' }, { headers: { ETag: '"v2"' } });
      await settle();

      expect(toasts()).toContain('The time zone is now \u2066Asia/Riyadh\u2069.');
      expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);

      // The next save, in either section, presents the new version.
      type('Acme Holdings');
      await settle();
      submit();
      await settle();

      const rename = http.expectOne(renameUrl);

      expect(rename.request.headers.get('If-Match')).toBe('"v2"');
      rename.flush(
        { ...acme, name: 'Acme Holdings', timeZone: 'Asia/Riyadh' },
        {
          headers: { ETag: '"v3"' },
        },
      );
      await settle();

      // Renaming does not put the zone back.
      expect(select().value).toBe('Asia/Riyadh');
    });

    it('says how many schedules move, asks first, and sends nothing if the reader keeps the zone', async () => {
      await open('workspace.manage', 'reports.view');
      stubDialogs(host());

      choose('Asia/Riyadh');
      await settle();
      submitZone();
      await settle();

      countRead(1);
      await settle();

      const dialog = host().querySelector('dialog[open]');

      expect(dialog?.textContent).toContain('Change the time zone to');
      expect(dialog?.textContent).toContain('Asia/Riyadh');
      expect(dialog?.textContent).toContain(
        '1 scheduled report moves to the next start of a day in the new zone',
      );

      answerConfirmation(host(), false);
      await settle();

      // verify() proves no PUT; the choice is still there to save later.
      expect(select().value).toBe('Asia/Riyadh');
    });

    it('asks a manager without reports.view with the count, and saves with the version it read', async () => {
      await open('workspace.manage');
      stubDialogs(host());

      choose('Europe/Berlin');
      await settle();
      submitZone();
      await settle();
      countRead(12);
      await settle();

      expect(host().querySelector('dialog[open]')?.textContent).toContain(
        '12 scheduled reports move',
      );

      answerConfirmation(host());
      await settle();

      const put = http.expectOne(zoneUrl);

      // The count's read is not adopted: the reader chose against "v1".
      expect(put.request.headers.get('If-Match')).toBe('"v1"');
      put.flush({ ...acme, timeZone: 'Europe/Berlin' }, { headers: { ETag: '"v2"' } });
      await settle();

      expect(toasts()).toContain('The time zone is now \u2066Europe/Berlin\u2069.');
    });

    it('asks without a number when the read carries no count', async () => {
      await open('workspace.manage');
      stubDialogs(host());

      choose('Asia/Riyadh');
      await settle();
      submitZone();
      await settle();
      countRead(undefined);
      await settle();

      expect(host().querySelector('dialog[open]')?.textContent).toContain(
        'Every report schedule moves',
      );
      answerConfirmation(host(), false);
      await settle();
    });

    it('asks without a number when the count cannot be read', async () => {
      await open('workspace.manage');
      stubDialogs(host());

      choose('Asia/Riyadh');
      await settle();
      submitZone();
      await settle();
      http.expectOne(tenantUrl).flush({}, { status: 503, statusText: 'Service Unavailable' });
      await settle();

      expect(host().querySelector('dialog[open]')?.textContent).toContain(
        'Every report schedule moves',
      );
      answerConfirmation(host(), false);
      await settle();
    });

    it('keeps the choice when the workspace changed elsewhere, and reloads beside it', async () => {
      await open('workspace.manage', 'reports.view');

      choose('Asia/Riyadh');
      await settle();
      submitZone();
      await settle();
      countRead(0);
      await settle();

      http
        .expectOne(zoneUrl)
        .flush({ code: 'precondition.failed' }, { status: 412, statusText: 'Precondition Failed' });
      await settle();

      expect(host().querySelector('[data-testid="zone-stale"]')).not.toBeNull();
      expect(select().value).toBe('Asia/Riyadh');

      host().querySelector<HTMLButtonElement>('[data-testid="zone-reload"]')!.click();
      await settle();
      http
        .expectOne(tenantUrl)
        .flush({ ...acme, timeZone: 'Europe/Berlin' }, { headers: { ETag: '"v5"' } });
      await settle();

      expect(host().querySelector('[data-testid="zone-stale"]')).toBeNull();
      expect(select().value).toBe('Asia/Riyadh');
      expect(host().querySelector('[data-testid="saved-zone"]')?.textContent).toContain(
        'Europe/Berlin',
      );
    });

    it('words workspace.time_zone.invalid by its code, never the server sentence', async () => {
      registerProblemWords(WORKSPACE_PROBLEM_WORDS);
      await open('workspace.manage', 'reports.view');

      choose('Asia/Riyadh');
      await settle();
      submitZone();
      await settle();
      countRead(0);
      await settle();

      http
        .expectOne(zoneUrl)
        .flush(
          { code: 'workspace.time_zone.invalid', detail: 'Unknown IANA time zone.' },
          { status: 400, statusText: 'Bad Request' },
        );
      await settle();

      const failure = host().querySelector('[data-testid="zone-failure"]')!.textContent ?? '';

      expect(failure).toContain('Nabra does not recognise that time zone.');
      expect(failure).not.toContain('IANA');
    });

    it('offers the browser zone as a suggestion that only chooses it', async () => {
      await open('workspace.manage');

      const suggestion = host().querySelector<HTMLButtonElement>(
        '[data-testid="time-zone-browser"]',
      );
      const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;

      if (browser === 'UTC') {
        // A browser in UTC has nothing to suggest beyond what is saved.
        expect(suggestion).toBeNull();

        return;
      }

      suggestion!.click();
      await settle();

      expect(select().value).toBe(browser);
      // verify() proves choosing sent nothing.
    });

    it('shows the zone as text, left to right, to somebody who cannot manage it', async () => {
      await open();

      expect(host().querySelector('[data-testid="time-zone-select"]')).toBeNull();
      expect(host().querySelector('[data-testid="save-zone"]')).toBeNull();

      const value = host().querySelector('[data-testid="time-zone-text"]')!;

      expect(value.textContent?.trim()).toBe('UTC');
      expect(value.getAttribute('dir')).toBe('ltr');
    });

    it('keeps the save button focusable with nothing to save', async () => {
      await open('workspace.manage');

      expect(saveZone().disabled).toBe(false);
      expect(saveZone().getAttribute('aria-disabled')).toBe('true');
      submitZone();
      await settle();
      // verify() proves nothing was sent.
    });
  });

  describe('zoneGroups', () => {
    it('groups by region, keeps zones it must, and drops UTC from the groups', () => {
      const groups = zoneGroups(['Africa/Cairo', 'Asia/Riyadh', 'Asia/Dubai', 'UTC'], 'riy', [
        'Etc/Old_Alias',
      ]);

      expect(groups).toEqual([
        { region: 'Asia', zones: ['Asia/Riyadh'] },
        { region: 'Etc', zones: ['Etc/Old_Alias'] },
      ]);
    });
  });
});
