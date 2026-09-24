import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ReportCatalog, ReportExecution, ReportExecutionPage, ReportsApi } from '../reports-api';
import { ReportsPage } from './reports-page';

/**
 * Generating and downloading reports.
 *
 * What matters: the page asks for nothing it may not have, it offers only what
 * the server said this member may generate, a request carries an idempotency
 * key, downloads are plain links to the API, and a failure is said in words.
 */
describe('ReportsPage', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const base = `/api/tenants/${tenantId}/reports`;

  const catalog: ReportCatalog = {
    reports: [
      {
        type: 'ConversationActivity',
        title: 'Conversation activity',
        description: 'Conversations started and messages sent.',
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

  const completed: ReportExecution = {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    type: 'ConversationActivity',
    title: 'Conversation activity',
    format: 'Csv',
    periodFrom: '2026-09-01',
    periodTo: '2026-10-01',
    periodLastDay: '2026-09-30',
    status: 'Completed',
    requestedByMe: true,
    scheduleId: null,
    scheduleName: null,
    requestedAt: '2026-10-01T08:00:00Z',
    startedAt: '2026-10-01T08:00:01Z',
    completedAt: '2026-10-01T08:00:02Z',
    failedAt: null,
    failureCode: null,
    failureReason: null,
    byteLength: 2048,
    expiresAt: '2026-10-31T08:00:02Z',
    canDownload: true,
  };

  const failed: ReportExecution = {
    ...completed,
    id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    status: 'Failed',
    scheduleName: '<img src=x onerror=alert(1)>',
    failureCode: 'report_too_large',
    failureReason: 'Report exceeds the supported export size. Choose a shorter period.',
    byteLength: null,
    canDownload: false,
  };

  const page = (items: readonly ReportExecution[]): ReportExecutionPage => ({
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
  });

  let fixture: ComponentFixture<ReportsPage>;
  let http: HttpTestingController;

  async function mount(...permissions: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [ReportsPage],
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
    fixture = TestBed.createComponent(ReportsPage);

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

  async function answerInitial(items: readonly ReportExecution[] = [completed]): Promise<void> {
    http.expectOne(`${base}/catalog`).flush(catalog);
    http.expectOne((request) => request.url === `${base}/executions`).flush(page(items));
    await settle();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  afterEach(() => http.verify());

  it('asks for nothing and explains itself without reports.view', async () => {
    await mount('analytics.view', 'reports.generate');

    http.expectNone(`${base}/catalog`);
    expect(text()).toContain('do not have access to reports');
  });

  it('lets a viewer download but not generate', async () => {
    await mount('reports.view', 'analytics.view');
    await answerInitial();

    expect(host().querySelector('[data-testid="no-generate"]')).not.toBeNull();
    expect(host().querySelector('[data-testid="generate"]')).toBeNull();
  });

  it('downloads through the API with a meaningful accessible name', async () => {
    await mount('reports.view', 'reports.generate', 'analytics.view');
    await answerInitial();

    const link = host().querySelector<HTMLAnchorElement>('[data-testid="download"]');

    expect(link).not.toBeNull();
    expect(link!.getAttribute('href')).toBe(`${base}/executions/${completed.id}/download`);
    expect(link!.getAttribute('aria-label')).toContain('Download Conversation activity');
    expect(link!.getAttribute('aria-label')).toContain('CSV');
  });

  it('states status in words and shows the failure reason, rendering names as text', async () => {
    await mount('reports.view', 'reports.generate', 'analytics.view');
    await answerInitial([completed, failed]);

    expect(text()).toContain('Ready');
    expect(text()).toContain('Failed');
    expect(text()).toContain('Report exceeds the supported export size.');
    expect(text()).toContain('Schedule: <img src=x onerror=alert(1)>');
    expect(host().querySelector('img')).toBeNull();
  });

  it('requests a report with an idempotency key and shows it at once', async () => {
    await mount('reports.view', 'reports.generate', 'analytics.view');
    await answerInitial([]);

    host().querySelector<HTMLButtonElement>('[data-testid="generate"]')!.click();
    await settle();

    const request = http.expectOne(
      (candidate) => candidate.method === 'POST' && candidate.url === base,
    );

    expect(request.request.body.reportType).toBe('ConversationActivity');
    expect(request.request.body.range).toBe('30d');
    expect(request.request.body.format).toBe('Csv');
    expect(request.request.body.clientRequestId).toMatch(/^[0-9a-f-]{36}$/);

    request.flush({ ...completed, status: 'Pending', canDownload: false });
    await settle();

    http
      .expectOne((candidate) => candidate.url === `${base}/executions`)
      .flush(page([{ ...completed, status: 'Pending', canDownload: false }]));
    await settle();

    expect(text()).toContain('Waiting');
  });

  it('refuses an explicit period longer than the platform allows before asking', async () => {
    await mount('reports.view', 'reports.generate', 'analytics.view');
    await answerInitial([]);

    const component = fixture.componentInstance as unknown as {
      form: { patchValue(value: object): void };
    };

    component.form.patchValue({ range: 'custom', from: '2024-01-01', through: '2025-06-01' });
    await settle();

    host().querySelector<HTMLButtonElement>('[data-testid="generate"]')!.click();
    await settle();

    http.expectNone((candidate) => candidate.method === 'POST');
    expect(host().querySelector('[data-testid="form-error"]')?.textContent).toContain(
      'at most 366 days',
    );
  });

  it('says so when this deployment cannot store reports', async () => {
    await mount('reports.view', 'reports.generate', 'analytics.view');

    http.expectOne(`${base}/catalog`).flush({ ...catalog, storageConfigured: false });
    http.expectOne((request) => request.url === `${base}/executions`).flush(page([]));
    await settle();

    expect(host().querySelector('[data-testid="no-storage"]')).not.toBeNull();
  });
});
