import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, map, mergeMap, throwError } from 'rxjs';
import { AppLanguage } from '@app/core/i18n/language';
import { formatLocalDate } from '@app/core/i18n/time-zone';
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

/**
 * The language a report file is written in (Phase 37.7). Anything else is
 * refused with `language.unsupported`.
 */
export type ReportLanguage = 'en' | 'ar';

/**
 * One of a report's request parameters, by code (Phase 37.6): `range` (`7d`,
 * `30d`, `90d`, `custom`) or `format` (the report's formats). A client words
 * each value by its code; a value it does not know is shown as its code.
 */
export interface ReportParameter {
  readonly code: string;
  readonly values: readonly string[];
}

/** One report this member may generate. */
export interface ReportCatalogEntry {
  /** The report's stable translation key; never changes. */
  readonly type: ReportType;

  /** English, kept for older clients; this one words the report by `type`. */
  readonly title: string;
  readonly description: string;
  readonly formats: readonly ReportFormat[];
  readonly requiredPermissions: readonly string[];
  readonly canSchedule: boolean;
  readonly parameters: readonly ReportParameter[];
}

export interface ReportCatalog {
  readonly reports: readonly ReportCatalogEntry[];
  readonly maxRangeDays: number;
  readonly presets: readonly string[];

  /** The workspace's zone: presets and explicit dates are its local days. */
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

  /** Inclusive first day, a local date of {@link timeZone}. */
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

  /**
   * The zone the period's dates are local dates of: the workspace's when the
   * report was asked for, kept even if the workspace's zone changed since.
   */
  readonly timeZone: string;

  /** The language the file is written in. */
  readonly language: string;
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

  /**
   * The file's language. Always sent: the form defaults to the language the
   * reader is using Nabra in (the owner's decision, 38.0 §4.3 #4).
   */
  readonly language: ReportLanguage;
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

  /** The language every report this schedule produces is written in. */
  readonly language: string;
}

export interface SaveReportSchedule {
  readonly name: string;
  readonly reportType: ReportType;
  readonly format: ReportFormat;
  readonly frequency: ReportFrequency;
  readonly enabled: boolean;

  /**
   * Always sent. A new schedule defaults to the reader's language; an edit
   * sends the schedule's own unless the reader changed it, so a colleague's
   * save never moves it to their language.
   */
  readonly language: ReportLanguage;
}

/** The values a report offers for one parameter, or null when it does not list that parameter. */
export function parameterValues(entry: ReportCatalogEntry, code: string): readonly string[] | null {
  return entry.parameters?.find((parameter) => parameter.code === code)?.values ?? null;
}

/**
 * The Reports endpoints.
 *
 * <strong>Asking is not downloading.</strong> A request is accepted and queued;
 * a worker produces the file; the list says when it is ready.
 *
 * <strong>Downloads go through this client</strong> (Phase 38 note 16, Phase
 * 39.7a), so a refusal (still being prepared, failed, expired, missing) reaches
 * the page as a problem with its `code` and is worded in the reader's language,
 * instead of the browser showing the API's JSON. A ready file is saved under
 * the name the server gave it.
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

  /** The API route a download reads. Never a file path. */
  downloadUrl(tenantId: string, executionId: string): string {
    return `${base(tenantId)}/executions/${executionId}/download`;
  }

  /**
   * Reads a generated report's file, with the name the server gave it.
   *
   * A refusal arrives as a blob too; it is read back into the problem the API
   * sent, so `toApiError` sees its `code` exactly as for any other route.
   */
  download(tenantId: string, executionId: string): Observable<ReportFile> {
    return this.http
      .get(this.downloadUrl(tenantId, executionId), { observe: 'response', responseType: 'blob' })
      .pipe(
        map((response) => ({
          blob: response.body ?? new Blob([]),
          fileName: fileNameFrom(response.headers.get('Content-Disposition')),
        })),
        catchError((failure: unknown) =>
          from(problemFromBlob(failure)).pipe(mergeMap((error) => throwError(() => error))),
        ),
      );
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

/** A downloaded report: its bytes and the name to save them under (null when the server gave none). */
export interface ReportFile {
  readonly blob: Blob;
  readonly fileName: string | null;
}

/**
 * The file name in a `Content-Disposition` header: the RFC 6266 `filename*`
 * (UTF-8, percent-encoded) when present, else the plain `filename`. Only the
 * last path segment is kept and control characters are dropped, so a header
 * can never name a folder.
 */
export function fileNameFrom(header: string | null): string | null {
  if (!header) {
    return null;
  }

  let name: string | null = null;
  const extended = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);

  if (extended) {
    try {
      name = decodeURIComponent(extended[1].trim().replace(/^"|"$/g, ''));
    } catch {
      name = null;
    }
  }

  if (name === null) {
    const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(header);

    name = plain ? (plain[2] ?? plain[1]).trim() : null;
  }

  const safe = name
    ?.split(/[\\/]/)
    .pop()
    // eslint-disable-next-line no-control-regex
    ?.replace(/[\u0000-\u001f\u007f]/g, '')
    .trim();

  return safe ? safe : null;
}

/**
 * A failed blob request, with the problem body parsed back out of the blob, so
 * the refusal's `code` reaches `toApiError`. Anything that is not JSON is left
 * as it was.
 */
export async function problemFromBlob(failure: unknown): Promise<unknown> {
  if (!(failure instanceof HttpErrorResponse) || !(failure.error instanceof Blob)) {
    return failure;
  }

  try {
    const body: unknown = JSON.parse(await failure.error.text());

    return new HttpErrorResponse({
      error: body,
      headers: failure.headers,
      status: failure.status,
      url: failure.url ?? undefined,
    });
  } catch {
    return failure;
  }
}

/**
 * A report's period, written out, with the last day included.
 *
 * The dates are local dates of the execution's zone, so they are written from
 * their parts and never re-read in the browser's zone; the zone itself is named
 * beside the period, where the screen shows it.
 */
export function periodLabel(from: string, lastDay: string, language: AppLanguage = 'en'): string {
  const format = (value: string): string => formatLocalDate(language, value, 'date');

  return from === lastDay ? format(from) : `${format(from)} – ${format(lastDay)}`;
}
