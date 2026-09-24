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

  it('says why the platform switched a schedule off', async () => {
    await mount('reports.view', 'reports.generate', 'reports.schedule', 'analytics.view');
    await answerInitial([{ ...schedule, enabled: false, disabledReason: 'owner_lost_permission' }]);

    expect(text()).toContain('Off');
    expect(text()).toContain('no longer has the permissions this report needs');
  });
});
