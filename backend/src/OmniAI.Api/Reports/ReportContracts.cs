using OmniAI.Application.Reports;

namespace OmniAI.Api.Reports;

/// <summary>Ask for a report.</summary>
/// <param name="Range"><c>7d</c>, <c>30d</c>, <c>90d</c>, or <c>custom</c> with both dates.</param>
/// <param name="To">Exclusive: the first day not covered, a local date of the workspace's zone.</param>
/// <param name="ClientRequestId">
/// Generated once per intended report by the client. Repeating it returns the
/// same execution instead of queuing another.
/// </param>
internal sealed record RequestReportRequest(
    string? ReportType,
    string? Format,
    string? Range,
    DateOnly? From,
    DateOnly? To,
    Guid? ClientRequestId);

/// <summary>Create a schedule, or replace one's editable fields.</summary>
internal sealed record SaveReportScheduleRequest(
    string? Name,
    string? ReportType,
    string? Format,
    string? Frequency,
    bool? Enabled);

internal sealed record SetReportScheduleEnabledRequest(bool? Enabled);

/// <summary>One report the caller may generate.</summary>
/// <param name="Type">
/// The stable translation key of the report (Phase 37.6, row 32): its name and
/// description are worded by the client from this code. It never changes.
/// </param>
/// <param name="Parameters">
/// What a request for this report may name, as codes with their allowed values
/// (Phase 37.6, row 32): <c>range</c> and <c>format</c>.
/// </param>
internal sealed record ReportCatalogEntryResponse(
    string Type,
    string Title,
    string Description,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> RequiredPermissions,
    bool CanSchedule,
    IReadOnlyList<ReportParameterResponse> Parameters);

/// <summary>One parameter of a report request, by code (Phase 37.6, row 32).</summary>
/// <param name="Code">A stable code: <c>range</c> or <c>format</c>.</param>
/// <param name="Values">The values it accepts, as the request spells them.</param>
internal sealed record ReportParameterResponse(string Code, IReadOnlyList<string> Values);

/// <summary>What the caller may generate, and within what bounds.</summary>
/// <param name="TimeZone">
/// The IANA zone report periods are counted in: the workspace's, <c>UTC</c> unless
/// somebody chose another (Phase 37.6).
/// </param>
internal sealed record ReportCatalogResponse(
    IReadOnlyList<ReportCatalogEntryResponse> Reports,
    int MaxRangeDays,
    IReadOnlyList<string> Presets,
    string TimeZone,
    bool StorageConfigured)
{
    public static ReportCatalogResponse From(ReportCatalogView catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new ReportCatalogResponse(
            [
                .. catalog.Reports.Select(entry => new ReportCatalogEntryResponse(
                    entry.Definition.Type.ToString(),
                    entry.Definition.Title,
                    entry.Definition.Description,
                    [.. entry.Definition.Formats.Select(format => format.ToString())],
                    [.. ReportCatalog.ToGenerate(entry.Definition.Type).Select(code => code.Value)],
                    entry.CanSchedule,
                    [
                        new ReportParameterResponse(
                            ReportCatalog.RangeParameter,
                            [.. catalog.PresetDays.Select(days => $"{days}d"), ReportCatalog.CustomRange]),
                        new ReportParameterResponse(
                            ReportCatalog.FormatParameter,
                            [.. entry.Definition.Formats.Select(format => format.ToString())]),
                    ])),
            ],
            catalog.MaxRangeDays,
            [.. catalog.PresetDays.Select(days => $"{days}d")],
            catalog.TimeZone,
            catalog.StorageConfigured);
    }
}

/// <summary>
/// One execution.
/// </summary>
/// <remarks>
/// <strong>No storage key, no path, no requester email.</strong> Who asked is
/// published only as "you" or "not you", and a scheduled report by its schedule's
/// name. The file is reached through the download route and nothing else.
/// </remarks>
internal sealed record ReportExecutionResponse(
    Guid Id,
    string Type,
    string Title,
    string Format,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly PeriodLastDay,
    string Status,
    bool RequestedByMe,
    Guid? ScheduleId,
    string? ScheduleName,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? FailedAt,
    string? FailureCode,
    string? FailureReason,
    long? ByteLength,
    DateTimeOffset? ExpiresAt,
    bool CanDownload,

    // Phase 37.6: the IANA zone periodFrom, periodTo and periodLastDay are local
    // dates of. UTC for every execution made before 37.6.
    string TimeZone)
{
    public static ReportExecutionResponse From(ReportExecutionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ReportExecutionResponse(
            view.Id,
            view.Type.ToString(),
            view.Title,
            view.Format.ToString(),
            view.PeriodFrom,
            view.PeriodTo,
            view.PeriodTo.AddDays(-1),
            view.Status.ToString(),
            view.RequestedByMe,
            view.ScheduleId,
            view.ScheduleName,
            view.RequestedAt,
            view.StartedAt,
            view.CompletedAt,
            view.FailedAt,
            view.FailureCode,
            view.FailureReason,
            view.ByteLength,
            view.ExpiresAt,
            view.CanDownload,
            view.TimeZone);
    }
}

internal sealed record ReportExecutionPageResponse(
    IReadOnlyList<ReportExecutionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ReportExecutionPageResponse From(ReportExecutionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ReportExecutionPageResponse(
            [.. page.Items.Select(ReportExecutionResponse.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

/// <summary>One schedule. Carries no recipients: delivery is into Reports only.</summary>
internal sealed record ReportScheduleResponse(
    Guid Id,
    string Name,
    string Type,
    string Title,
    string Format,
    string Frequency,
    string RangePreset,
    bool Enabled,
    DateTimeOffset NextRunAt,
    bool OwnedByMe,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DisabledReason,
    ReportExecutionResponse? LastExecution,
    bool CanEdit)
{
    public static ReportScheduleResponse From(ReportScheduleView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ReportScheduleResponse(
            view.Id,
            view.Name,
            view.Type.ToString(),
            view.Title,
            view.Format.ToString(),
            view.Frequency.ToString(),
            view.Frequency == Domain.Reports.ReportFrequency.Weekly ? "PreviousWeek" : "PreviousMonth",
            view.Enabled,
            view.NextRunAt,
            view.OwnedByMe,
            view.CreatedAt,
            view.UpdatedAt,
            view.DisabledReason,
            view.LastExecution is null ? null : ReportExecutionResponse.From(view.LastExecution),
            view.CanEdit);
    }
}

internal sealed record ReportScheduleListResponse(IReadOnlyList<ReportScheduleResponse> Items);
