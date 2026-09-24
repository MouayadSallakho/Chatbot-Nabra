import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { Versioned, withEntityTag } from '@app/core/api/versioned';

/** Every report the platform can produce. The server decides which a member sees. */
export type ReportType =
  | 'ConversationActivity'
  | 'ChannelUsage'
  | 'AiHumanHandling'
  | 'CustomerActivity'
  | 'AgentActivity'
  | 'ProviderUsage'
  | 'ProviderCost'
  | 'WhatsAppDelivery';

export type ReportFormat = 'Csv' | 'Xlsx' | 'Pdf';

export type ReportStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Expired';

export type ReportFrequency = 'Weekly' | 'Monthly';

/** A period preset, or explicit dates. */
export type ReportRange = '7d' | '30d' | '90d' | 'custom';

/** One report this member may generate. */
export interface ReportCatalogEntry {
  readonly type: ReportType;
  readonly title: string;
  readonly description: string;
  readonly formats: readonly ReportFormat[];
  readonly requiredPermissions: readonly string[];
  readonly canSchedule: boolean;
}

export interface ReportCatalog {
  readonly reports: readonly ReportCatalogEntry[];
  readonly maxRangeDays: number;
  readonly presets: readonly string[];
  readonly timeZone: string;

  /** False on a deployment with no report storage: nothing can be generated there. */
  readonly storageConfigured: boolean;
}

/**
 * One generated, or generating, report.
 *
 * <strong>No storage key and no path.</strong> The file is reached through the
 * download route and nothing else, and who asked is published only as "you" or
 * "not you" — never a colleague's email address.
 */
export interface ReportExecution {
  readonly id: string;
  readonly type: ReportType;
  readonly title: string;
  readonly format: ReportFormat;

  /** Inclusive first day, UTC. */
  readonly periodFrom: string;

  /** Exclusive: the first day not covered. */
  readonly periodTo: string;

  /** Inclusive last day, for display. */
  readonly periodLastDay: string;
  readonly status: ReportStatus;
  readonly requestedByMe: boolean;
  readonly scheduleId: string | null;
  readonly scheduleName: string | null;
  readonly requestedAt: string;
  readonly startedAt: string | null;
  readonly completedAt: string | null;
  readonly failedAt: string | null;
  readonly failureCode: string | null;

  /** A sentence the server wrote for this failure. Never exception text. */
  readonly failureReason: string | null;
  readonly byteLength: number | null;
  readonly expiresAt: string | null;
  readonly canDownload: boolean;
}

export interface ReportExecutionPage {
  readonly items: readonly ReportExecution[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** What asking for a report sends. */
export interface ReportRequest {
  readonly reportType: ReportType;
  readonly format: ReportFormat;
  readonly range: ReportRange;

  /** ISO date, only with a custom range. */
  readonly from?: string;

  /** ISO date, exclusive, only with a custom range. */
  readonly to?: string;

  /**
   * One per intended report. Sending the same one again returns the same
   * execution rather than generating a second — which is what makes a double
   * click harmless, whatever the button does.
   */
  readonly clientRequestId: string;
}

export interface ReportSchedule {
  readonly id: string;
  readonly name: string;
  readonly type: ReportType;
  readonly title: string;
  readonly format: ReportFormat;
  readonly frequency: ReportFrequency;
  readonly rangePreset: 'PreviousWeek' | 'PreviousMonth';
  readonly enabled: boolean;
  readonly nextRunAt: string;
  readonly ownedByMe: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;

  /** Set when the platform switched the schedule off, and why. */
  readonly disabledReason: string | null;
  readonly lastExecution: ReportExecution | null;
  readonly canEdit: boolean;
}

export interface SaveReportSchedule {
  readonly name: string;
  readonly reportType: ReportType;
  readonly format: ReportFormat;
  readonly frequency: ReportFrequency;
  readonly enabled: boolean;
}

/**
 * The Reports endpoints.
 *
 * <strong>Asking is not downloading.</strong> A request is accepted and queued;
 * a worker produces the file; the list says when it is ready. Downloads are
 * ordinary links to the API, so the browser streams the file with the session it
 * already has and nothing is held in this page's memory.
 */
@Injectable()
export class ReportsApi {
  private readonly http = inject(HttpClient);

  catalog(tenantId: string): Observable<ReportCatalog> {
    return this.http.get<ReportCatalog>(`${base(tenantId)}/catalog`);
  }

  request(tenantId: string, request: ReportRequest): Observable<ReportExecution> {
    return this.http.post<ReportExecution>(base(tenantId), request);
  }

  list(tenantId: string, page: number, pageSize: number): Observable<ReportExecutionPage> {
    return this.http.get<ReportExecutionPage>(`${base(tenantId)}/executions`, {
      params: { page, pageSize },
    });
  }

  get(tenantId: string, executionId: string): Observable<ReportExecution> {
    return this.http.get<ReportExecution>(`${base(tenantId)}/executions/${executionId}`);
  }

  /** The API route a download link points at. Never a file path. */
  downloadUrl(tenantId: string, executionId: string): string {
    return `${base(tenantId)}/executions/${executionId}/download`;
  }

  schedules(tenantId: string): Observable<readonly ReportSchedule[]> {
    return this.http
      .get<{ items: readonly ReportSchedule[] }>(`${base(tenantId)}/schedules`)
      .pipe(map((response) => response.items));
  }

  getSchedule(tenantId: string, scheduleId: string): Observable<Versioned<ReportSchedule>> {
    return this.http
      .get<ReportSchedule>(`${base(tenantId)}/schedules/${scheduleId}`, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  createSchedule(
    tenantId: string,
    schedule: SaveReportSchedule,
  ): Observable<Versioned<ReportSchedule>> {
    return this.http
      .post<ReportSchedule>(`${base(tenantId)}/schedules`, schedule, { observe: 'response' })
      .pipe(map(withEntityTag));
  }

  updateSchedule(
    tenantId: string,
    scheduleId: string,
    schedule: SaveReportSchedule,
    etag: string,
  ): Observable<Versioned<ReportSchedule>> {
    return this.http
      .put<ReportSchedule>(`${base(tenantId)}/schedules/${scheduleId}`, schedule, {
        observe: 'response',
        headers: { 'If-Match': etag },
      })
      .pipe(map(withEntityTag));
  }

  setScheduleEnabled(
    tenantId: string,
    scheduleId: string,
    enabled: boolean,
    etag: string,
  ): Observable<Versioned<ReportSchedule>> {
    return this.http
      .put<ReportSchedule>(
        `${base(tenantId)}/schedules/${scheduleId}/enabled`,
        { enabled },
        { observe: 'response', headers: { 'If-Match': etag } },
      )
      .pipe(map(withEntityTag));
  }
}

function base(tenantId: string): string {
  return `/api/tenants/${tenantId}/reports`;
}

/** The words for a format, as a person says them. */
export function formatLabel(format: ReportFormat): string {
  return format === 'Xlsx' ? 'Excel (XLSX)' : format.toUpperCase();
}

/** A byte count a person can read. */
export function sizeLabel(bytes: number | null): string {
  if (bytes === null) {
    return '—';
  }

  if (bytes < 1024) {
    return `${bytes} B`;
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** A UTC date range, written out, with the last day included. */
export function periodLabel(from: string, lastDay: string): string {
  const format = (value: string): string =>
    new Date(`${value}T00:00:00Z`).toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });

  return from === lastDay ? format(from) : `${format(from)} – ${format(lastDay)}`;
}
