using System.Globalization;
using OmniAI.Application.Authorization;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>One catalogue entry the caller may generate.</summary>
public sealed record ReportCatalogEntry(ReportDefinition Definition, bool CanSchedule);

/// <summary>What the reports catalogue publishes to one member.</summary>
/// <param name="TimeZone">The zone report periods are counted in: the workspace's (Phase 37.6).</param>
public sealed record ReportCatalogView(
    IReadOnlyList<ReportCatalogEntry> Reports,
    int MaxRangeDays,
    IReadOnlyList<int> PresetDays,
    bool StorageConfigured,
    string TimeZone);

/// <summary>An execution as the API publishes it.</summary>
/// <param name="TimeZone">The zone its period's days are local days of (Phase 37.6).</param>
public sealed record ReportExecutionView(
    Guid Id,
    ReportType Type,
    string Title,
    ReportFormat Format,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    ReportExecutionStatus Status,
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
    string TimeZone);

/// <summary>One page of executions.</summary>
public sealed record ReportExecutionPage(
    IReadOnlyList<ReportExecutionView> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
}

/// <summary>Ask for a report.</summary>
/// <param name="Range"><c>7d</c>, <c>30d</c>, <c>90d</c>, or <c>custom</c> with both dates.</param>
/// <param name="To">Exclusive: the first day not covered.</param>
public sealed record RequestReportCommand(
    Guid TenantId,
    string? ReportType,
    string? Format,
    string? Range,
    DateOnly? From,
    DateOnly? To,
    Guid? ClientRequestId);

/// <summary>A file ready to stream, and what to call it.</summary>
public sealed record ReportDownload(Stream Content, string ContentType, string FileName, long ByteLength, string Sha256);

/// <summary>
/// Requesting, listing and downloading reports.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Three permissions, none implying another, and the data's own on
/// top.</strong> The catalogue and the list need <c>reports.view</c>; asking for a
/// report needs <c>reports.generate</c>; and every report additionally needs what
/// its data needs, at request time and again at download time. See
/// <see cref="ReportCatalog"/> for the matrix.
/// </para>
/// <para>
/// <strong>Asking is not generating.</strong> A request is recorded and answered
/// at once; a background worker produces the file. Nothing here reads a single
/// conversation.
/// </para>
/// <para>
/// <strong>Periods are local days of the workspace's zone (Phase 37.6).</strong>
/// The zone is read when the report is asked for and kept on the execution, so
/// the report covers the days it was asked for even if the zone changes before it
/// is generated.
/// </para>
/// </remarks>
public sealed class ReportHandlers(
    TenantAuthorizer authorizer,
    IReportExecutionStore executions,
    IReportArtifactStore artifacts,
    IWorkspaceTimeZones timeZones,
    TimeProvider clock)
{
    /// <summary>The presets a caller may name instead of two dates.</summary>
    public static readonly IReadOnlyList<int> PresetDays = [7, 30, 90];

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IReportExecutionStore _executions =
        executions ?? throw new ArgumentNullException(nameof(executions));

    private readonly IReportArtifactStore _artifacts =
        artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    private readonly IWorkspaceTimeZones _timeZones =
        timeZones ?? throw new ArgumentNullException(nameof(timeZones));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>The catalogue, or <see langword="null"/> for a non-member.</returns>
    /// <remarks>Only the reports this member may actually generate are listed.</remarks>
    public async Task<ReportCatalogView?> CatalogAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        TenantId workspace = TenantId.From(tenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsView, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return null;
        }

        WorkspaceTimeZone zone = await _timeZones.OfAsync(workspace, cancellationToken).ConfigureAwait(false);

        return new ReportCatalogView(
            [
                .. ReportCatalog.All
                    .Where(definition => ReportCatalog.Holds(held, ReportCatalog.ToGenerate(definition.Type)))
                    .Select(definition => new ReportCatalogEntry(
                        definition,
                        ReportCatalog.Holds(held, ReportCatalog.ToSchedule(definition.Type)))),
            ],
            ReportPeriod.MaxDays,
            PresetDays,
            _artifacts.IsConfigured,
            zone.Name);
    }

    /// <returns>The recorded execution, or <see langword="null"/> for a non-member.</returns>
    /// <exception cref="DomainValidationException">The request is malformed or out of bounds.</exception>
    /// <exception cref="PermissionDeniedException">The caller may not generate this report.</exception>
    /// <exception cref="ReportRequestConflictException">The request id was used for a different report.</exception>
    /// <exception cref="ReportStorageUnavailableException">There is nowhere to put the file.</exception>
    public async Task<(ReportExecutionView Execution, bool Created)?> RequestAsync(
        RequestReportCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                tenantId, PlatformPermissions.ReportsGenerate, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return null;
        }

        ReportType type = ReportRequestParsing.Type(command.ReportType);
        ReportFormat format = ReportRequestParsing.Format(command.Format);

        RequireAll(held, ReportCatalog.ToGenerate(type));

        if (!ReportCatalog.For(type).Supports(format))
        {
            throw new DomainValidationException("That report is not available in that format.");
        }

        if (command.ClientRequestId is not Guid clientRequestId || clientRequestId == Guid.Empty)
        {
            throw new DomainValidationException("A clientRequestId is required, so a repeated request is not generated twice.");
        }

        DateTimeOffset now = _clock.GetUtcNow();
        WorkspaceTimeZone zone = await _timeZones.OfAsync(tenantId, cancellationToken).ConfigureAwait(false);
        ReportPeriod period = ReportRequestParsing.Period(command.Range, command.From, command.To, now, zone);

        if (!_artifacts.IsConfigured)
        {
            throw new ReportStorageUnavailableException();
        }

        UserId requester = _authorizer.RequireCurrentUser();

        (ReportExecution execution, bool created) = await _executions
            .AddOrGetAsync(
                ReportExecution.Request(tenantId, type, format, period, requester, clientRequestId, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (!created && !execution.SameRequestAs(type, format, period))
        {
            throw new ReportRequestConflictException();
        }

        ReportExecutionRecord record = await _executions
                .FindAsync(tenantId, execution.Id, cancellationToken)
                .ConfigureAwait(false)
            ?? new ReportExecutionRecord(execution, null, null, null);

        return (View(record, held, requester, now), created);
    }

    public async Task<ReportExecutionPage?> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        TenantId workspace = TenantId.From(tenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsView, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return null;
        }

        int number = page ?? 1;
        int size = pageSize ?? ReportExecutionPage.DefaultPageSize;

        if (number < 1)
        {
            throw new DomainValidationException("Page must be 1 or greater.");
        }

        if (size < 1 || size > ReportExecutionPage.MaxPageSize)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Page size must be between 1 and {ReportExecutionPage.MaxPageSize}."));
        }

        (IReadOnlyList<ReportExecutionRecord> items, int total) = await _executions
            .ListAsync(workspace, number, size, cancellationToken)
            .ConfigureAwait(false);

        UserId me = _authorizer.RequireCurrentUser();
        DateTimeOffset now = _clock.GetUtcNow();

        return new ReportExecutionPage([.. items.Select(item => View(item, held, me, now))], number, size, total);
    }

    public async Task<ReportExecutionView?> GetAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        TenantId workspace = TenantId.From(tenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsView, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || executionId == Guid.Empty)
        {
            return null;
        }

        ReportExecutionRecord? record = await _executions
            .FindAsync(workspace, ReportExecutionId.From(executionId), cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? null
            : View(record, held, _authorizer.RequireCurrentUser(), _clock.GetUtcNow());
    }

    /// <returns>The file, or <see langword="null"/> when there is no such report in this workspace.</returns>
    /// <exception cref="PermissionDeniedException">The caller may not read this report's data.</exception>
    /// <exception cref="ReportNotDownloadableException">It has no file, and says why.</exception>
    public async Task<ReportDownload?> DownloadAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        TenantId workspace = TenantId.From(tenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsView, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || executionId == Guid.Empty)
        {
            return null;
        }

        ReportExecutionRecord? record = await _executions
            .FindAsync(workspace, ReportExecutionId.From(executionId), cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return null;
        }

        ReportExecution execution = record.Execution;

        // The data's own permission, checked now rather than when the report was
        // made: somebody who has since lost customers.view must not be able to
        // download a customer list generated while they still held it.
        RequireAll(held, ReportCatalog.ToDownload(execution.Type));

        if (execution.Status != ReportExecutionStatus.Completed)
        {
            throw new ReportNotDownloadableException(execution.Status);
        }

        ReportArtifact? artifact = await _executions
            .FindArtifactAsync(workspace, execution.Id, cancellationToken)
            .ConfigureAwait(false);

        if (artifact is null || artifact.ExpiresAt <= _clock.GetUtcNow())
        {
            throw new ReportNotDownloadableException(ReportExecutionStatus.Expired);
        }

        Stream? content = await _artifacts
            .OpenReadAsync(artifact.StorageKey, cancellationToken)
            .ConfigureAwait(false);

        if (content is null)
        {
            // The record says the file exists and storage does not have it. Made
            // truthful now — the execution becomes Expired and its artifact row
            // goes — so the list stops offering a Download that cannot work.
            await _executions.MarkExpiredAsync(artifact, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);

            throw ReportNotDownloadableException.Missing();
        }

        return new ReportDownload(
            content,
            artifact.ContentType,
            ReportCatalog.FileName(execution.Type, execution.Period, execution.Format),
            artifact.ByteLength,
            artifact.Sha256);
    }

    internal static ReportExecutionView View(
        ReportExecutionRecord record,
        IReadOnlyCollection<PermissionCode> held,
        UserId viewer,
        DateTimeOffset now)
    {
        ReportExecution execution = record.Execution;
        ReportPeriod period = execution.Period;

        bool downloadable = execution.Status == ReportExecutionStatus.Completed
            && record.ExpiresAt is DateTimeOffset expires
            && expires > now
            && ReportCatalog.Holds(held, ReportCatalog.ToDownload(execution.Type));

        return new ReportExecutionView(
            execution.Id.Value,
            execution.Type,
            ReportCatalog.For(execution.Type).Title,
            execution.Format,
            period.FromDate,
            period.ToExclusiveDate,
            execution.Status,
            execution.RequestedBy == viewer,
            execution.ScheduleId?.Value,
            record.ScheduleName,
            execution.RequestedAt,
            execution.StartedAt,
            execution.CompletedAt,
            execution.FailedAt,
            execution.FailureCode,
            execution.Status == ReportExecutionStatus.Failed ? ReportFailures.Describe(execution.FailureCode) : null,
            execution.Status == ReportExecutionStatus.Completed ? record.ByteLength : null,
            execution.Status == ReportExecutionStatus.Completed ? record.ExpiresAt : null,
            downloadable,
            execution.TimeZone.Name);
    }

    /// <summary>Membership, then a base permission; returns everything the caller holds.</summary>
    private async Task<IReadOnlyList<PermissionCode>?> RequireAsync(
        TenantId tenantId,
        PermissionCode permission,
        CancellationToken cancellationToken)
    {
        if (!await _authorizer.RequireAsync(tenantId, permission, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _authorizer.EffectivePermissionsAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }

    internal static void RequireAll(IReadOnlyCollection<PermissionCode> held, IEnumerable<PermissionCode> required)
    {
        foreach (PermissionCode code in required)
        {
            if (!held.Contains(code))
            {
                throw PermissionDeniedException.ForPermission(code);
            }
        }
    }
}

/// <summary>Reading a report request's wire values, strictly.</summary>
internal static class ReportRequestParsing
{
    public static ReportType Type(string? value) =>
        Parse<ReportType>(value, "That report type is not in the catalogue.");

    public static ReportFormat Format(string? value) =>
        Parse<ReportFormat>(value, "Choose a format: Csv, Xlsx or Pdf.");

    public static ReportFrequency Frequency(string? value) =>
        Parse<ReportFrequency>(value, "A schedule runs Weekly or Monthly.");

    /// <summary>
    /// A preset or an explicit period, never both and never neither.
    /// </summary>
    /// <remarks>
    /// An explicit period may include today — the report is then a snapshot of a
    /// day still in progress, stamped with when it was generated — but nothing
    /// after it. Dates are local dates of <paramref name="timeZone"/>, the
    /// workspace's zone, and so is "today".
    /// </remarks>
    public static ReportPeriod Period(
        string? range,
        DateOnly? from,
        DateOnly? to,
        DateTimeOffset now,
        WorkspaceTimeZone timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        string preset = range?.Trim().ToLowerInvariant() ?? string.Empty;

        if (preset is "7d" or "30d" or "90d")
        {
            if (from is not null || to is not null)
            {
                throw new DomainValidationException("Give either a preset range or two dates, not both.");
            }

            return ReportPeriod.LastDays(int.Parse(preset[..^1], CultureInfo.InvariantCulture), now, timeZone);
        }

        if (preset is not ("custom" or ""))
        {
            throw new DomainValidationException("Range must be 7d, 30d, 90d or custom.");
        }

        if (from is not DateOnly start || to is not DateOnly end)
        {
            throw new DomainValidationException("A custom range needs both a from and a to date.");
        }

        DateOnly tomorrow = timeZone.DateOf(now).AddDays(1);

        if (end > tomorrow)
        {
            throw new DomainValidationException("A report period cannot extend into the future.");
        }

        return ReportPeriod.Create(start, end, timeZone);
    }

    private static T Parse<T>(string? value, string refusal)
        where T : struct, Enum
    {
        // Names only. Enum.TryParse would also accept "7" or "1,2", which would let
        // a caller address a value by number that nobody meant to publish.
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !char.IsAsciiLetter(character))
            || !Enum.TryParse(value, ignoreCase: true, out T parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new DomainValidationException(refusal);
        }

        return parsed;
    }
}
