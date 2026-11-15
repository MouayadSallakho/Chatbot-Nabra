import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ReportCatalog, ReportSchedule, ReportsApi } from '../reports-api';
import { ReportSchedules } from './report-schedules';

/**
 * Report schedules.
 *
 * What matters: delivery is promised only into Reports, there is no recipient
 * field, edits carry the version they were read at, and a stale edit is said
 * plainly with a way to recover.
 */
describe('ReportSchedules', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const base = `/api/tenants/${tenantId}/reports`;

  const catalog: ReportCatalog = {
    reports: [
      {
        type: 'ChannelUsage',
        title: 'Channel usage',
        description: 'Conversations and messages per channel.',
        formats: ['Csv', 'Xlsx', 'Pdf'],
        requiredPermissions: ['reports.generate', 'analytics.view'],
        canSchedule: true,
        parameters: [
          { code: 'range', values: ['7d', '30d', '90d', 'custom'] },
          { code: 'format', values: ['Csv', 'Xlsx', 'Pdf'] },
        ],
      },
    ],
    maxRangeDays: 366,
    presets: ['7d', '30d', '90d'],
    timeZone: 'UTC',
    storageConfigured: true,
  };

  const schedule: ReportSchedule = {
    id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    name: 'Weekly channels',
    type: 'ChannelUsage',
    title: 'Channel usage',
    format: 'Xlsx',
    frequency: 'Weekly',
    rangePreset: 'PreviousWeek',
    enabled: true,
    nextRunAt: '2026-09-28T00:00:00Z',
    ownedByMe: true,
    createdAt: '2026-09-24T10:00:00Z',
    updatedAt: '2026-09-24T10:00:00Z',
    disabledReason: null,
    lastExecution: null,
    canEdit: true,
    language: 'en',
  };

  let fixture: ComponentFixture<ReportSchedules>;
  let http: HttpTestingController;

  async function mount(...permissions: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ReportSchedules],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ReportsApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportSchedules);

    await settle();
  }

  /**
   * Lets the page's promise chains finish. Loading awaits two requests at once,
   * which takes more than one turn to resolve after both are answered.
   */
  async function settle(): Promise<void> {
    for (let turn = 0; turn < 3; turn++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve));
    }

    fixture.detectChanges();
  }

  async function answerInitial(schedules: readonly ReportSchedule[] = [schedule]): Promise<void> {
    http.expectOne(`${base}/catalog`).flush(catalog);
    http.expectOne(`${base}/schedules`).flush({ items: schedules });
    await settle();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  afterEach(() => http.verify());

  it('promises delivery into Reports only, and offers no recipient field', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial();

    expect(host().querySelector('[data-testid="delivery-note"]')?.textContent).toContain(
      'Generated reports are available in Reports.',
    );

    host().querySelector<HTMLButtonElement>('[data-testid="new-schedule"]')!.click();
    await settle();

    expect(host().querySelector('#schedule-name')).not.toBeNull();
    expect(text().toLowerCase()).not.toContain('recipient');
    expect(host().querySelector('input[type="email"]')).toBeNull();
  });

  it('shows the next run and offers no controls without reports.schedule', async () => {
    await mount('reports.view', 'analytics.view');
    await answerInitial([{ ...schedule, canEdit: false }]);

    expect(text()).toContain('Weekly channels');
    expect(text()).toContain('Weekly · previous 7 days');
    expect(host().querySelector('[data-testid="new-schedule"]')).toBeNull();
    expect(host().querySelector('[data-testid="toggle"]')).toBeNull();
  });

  it('edits with the version it read, and explains a stale save', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial();

    host()
      .querySelector<HTMLButtonElement>('button[aria-label="Edit schedule Weekly channels"]')!
      .click();
    await settle();

    http
      .expectOne(`${base}/schedules/${schedule.id}`)
      .flush(schedule, { headers: { ETag: '"v1"' } });
    await settle();

    host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
    await settle();

    const save = http.expectOne(
      (request) => request.method === 'PUT' && request.url === `${base}/schedules/${schedule.id}`,
    );

    expect(save.request.headers.get('If-Match')).toBe('"v1"');
    // The schedule's own language is kept, not replaced by the editor's.
    expect(save.request.body.language).toBe('en');

    save.flush(
      { title: 'This schedule changed', status: 412 },
      { status: 412, statusText: 'Precondition Failed' },
    );
    await settle();

    expect(host().querySelector('[data-testid="save-error"]')?.textContent).toContain(
      'changed somewhere else',
    );
    expect(host().querySelector('[data-testid="reload"]')).not.toBeNull();
  });

  it('moves focus into the form and back to what opened it', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial();
    // Focus only lands on elements in the document.
    document.body.append(host());

    host().querySelector<HTMLButtonElement>('[data-testid="new-schedule"]')!.click();
    await settle();

    expect(document.activeElement).toBe(host().querySelector('#schedule-name'));

    // An empty name is refused, and focus stays on the field it is about.
    host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
    await settle();

    expect(host().querySelector('#schedule-name-error')).not.toBeNull();
    expect(document.activeElement).toBe(host().querySelector('#schedule-name'));

    [...host().querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Cancel')!
      .click();
    await settle();

    // The button that opened the form was removed while it was open.
    expect(document.activeElement).toBe(host().querySelector('[data-testid="new-schedule"]'));

    const edit = host().querySelector<HTMLButtonElement>(
      'button[aria-label="Edit schedule Weekly channels"]',
    )!;
    edit.click();
    await settle();
    http
      .expectOne(`${base}/schedules/${schedule.id}`)
      .flush(schedule, { headers: { ETag: '"v1"' } });
    await settle();

    expect(document.activeElement).toBe(host().querySelector('#schedule-name'));

    host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
    await settle();
    http
      .expectOne((request) => request.method === 'PUT')
      .flush(schedule, { headers: { ETag: '"v2"' } });
    await settle();
    http.expectOne(`${base}/schedules`).flush({ items: [schedule] });
    await settle();

    expect(document.activeElement).toBe(
      host().querySelector('button[aria-label="Edit schedule Weekly channels"]'),
    );

    host().remove();
  });

  it('says why the platform switched a schedule off', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial([{ ...schedule, enabled: false, disabledReason: 'owner_lost_permission' }]);

    expect(text()).toContain('Off');
    expect(text()).toContain('no longer has the permissions this report needs');
  });

  it('asks before an open editor with edits is left, and only then', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial();

    const page = fixture.componentInstance;

    expect(page.hasUnsavedChanges()).toBe(false);

    host().querySelector<HTMLButtonElement>('[data-testid="new-schedule"]')!.click();
    await settle();

    expect(page.hasUnsavedChanges()).toBe(false);

    const name = host().querySelector<HTMLInputElement>('#schedule-name')!;

    name.value = 'Monday numbers';
    name.dispatchEvent(new Event('input'));
    await settle();

    expect(page.hasUnsavedChanges()).toBe(true);

    [...host().querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Cancel')!
      .click();
    await settle();

    expect(page.hasUnsavedChanges()).toBe(false);
  });

  describe('the file language (E8)', () => {
    const owner = ['reports.view', 'reports.generate', 'reports.schedule', 'analytics.view'];

    it('starts a new schedule in the language the reader uses, and sends it', async () => {
      await mount(...owner);
      await answerInitial([]);

      host().querySelector<HTMLButtonElement>('[data-testid="new-schedule"]')!.click();
      await settle();

      expect(
        host().querySelector<HTMLInputElement>('[data-testid="schedule-language-en"]')!.checked,
      ).toBe(true);

      const name = host().querySelector<HTMLInputElement>('#schedule-name')!;

      name.value = 'Monthly in Arabic';
      name.dispatchEvent(new Event('input'));
      host().querySelector<HTMLInputElement>('[data-testid="schedule-language-ar"]')!.click();
      host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
      await settle();

      const create = http.expectOne(
        (request) => request.method === 'POST' && request.url === `${base}/schedules`,
      );

      expect(create.request.body).toEqual(
        expect.objectContaining({ name: 'Monthly in Arabic', language: 'ar' }),
      );

      create.flush(
        { ...schedule, name: 'Monthly in Arabic', language: 'ar' },
        {
          headers: { ETag: '"v1"' },
        },
      );
      await settle();
      http.expectOne(`${base}/schedules`).flush({ items: [] });
      await settle();
    });

    it('opens an edit in the language of the schedule, whatever the reader uses', async () => {
      await mount(...owner);
      await answerInitial([{ ...schedule, language: 'ar' }]);

      host()
        .querySelector<HTMLButtonElement>('button[aria-label="Edit schedule Weekly channels"]')!
        .click();
      await settle();
      http
        .expectOne(`${base}/schedules/${schedule.id}`)
        .flush({ ...schedule, language: 'ar' }, { headers: { ETag: '"v1"' } });
      await settle();

      expect(
        host().querySelector<HTMLInputElement>('[data-testid="schedule-language-ar"]')!.checked,
      ).toBe(true);

      host().querySelector<HTMLButtonElement>('[data-testid="save"]')!.click();
      await settle();

      const save = http.expectOne((request) => request.method === 'PUT');

      expect(save.request.body.language).toBe('ar');

      save.flush({ ...schedule, language: 'ar' }, { headers: { ETag: '"v2"' } });
      await settle();
      http.expectOne(`${base}/schedules`).flush({ items: [{ ...schedule, language: 'ar' }] });
      await settle();
    });

    it('lists the file language of each schedule', async () => {
      await mount(...owner);
      await answerInitial([{ ...schedule, language: 'ar' }]);

      expect(
        host()
          .querySelector(`[data-testid="schedule-language-${schedule.id}"]`)
          ?.textContent?.trim(),
      ).toBe('Arabic file');
    });

    it('says each state as a chip with a word and its own marker (Phase 39.7a)', async () => {
      await mount(...owner);
      await answerInitial([schedule, { ...schedule, id: 'off-1', enabled: false }]);

      const chip = (id: string): Element | null =>
        host().querySelector(`[data-testid="schedule-${id}"] td:nth-child(5) app-chip`);

      expect(chip(schedule.id)?.textContent?.trim()).toBe('On');
      expect(chip(schedule.id)?.getAttribute('data-tone')).toBe('success');
      expect(chip('off-1')?.textContent?.trim()).toBe('Off');
      expect(chip('off-1')?.getAttribute('data-tone')).toBe('draft');
      expect(
        host().querySelector(`[data-testid="schedule-language-${schedule.id}"]`)?.tagName,
      ).toBe('APP-CHIP');
    });
  });

  describe('the workspace time zone (E2)', () => {
    async function answerIn(zone: string, schedules: readonly ReportSchedule[]): Promise<void> {
      http.expectOne(`${base}/catalog`).flush({ ...catalog, timeZone: zone });
      http.expectOne(`${base}/schedules`).flush({ items: schedules });
      await settle();
    }

    it('shows the next run as it reads in the workspace zone, and names that zone', async () => {
      await mount('reports.view', 'analytics.view');
      // Riyadh's Monday starts at 21:00 UTC on the Sunday.
      await answerIn('Asia/Riyadh', [{ ...schedule, nextRunAt: '2026-09-27T21:00:00Z' }]);

      const row = host().querySelector(`[data-testid="schedule-${schedule.id}"]`)!;

      expect(row.textContent).toContain('Sep 28, 2026, 12:00 AM');
      expect(host().querySelector('[data-testid="next-run-heading"]')?.textContent).toContain(
        'Asia/Riyadh',
      );
      expect(host().querySelector('[data-testid="zone-note"]')?.textContent).toContain(
        'Days and times on this page are in',
      );
      expect(text()).not.toContain('UTC');
    });

    it('keeps "UTC" for a workspace that never chose a zone', async () => {
      await mount('reports.view', 'analytics.view');
      await answerIn('UTC', [schedule]);

      expect(host().querySelector('[data-testid="next-run-heading"]')?.textContent?.trim()).toBe(
        'Next run (UTC)',
      );
      expect(host().querySelector('[data-testid="zone-note"]')?.textContent?.trim()).toBe(
        'Days and times on this page are in UTC.',
      );
    });
  });
});
