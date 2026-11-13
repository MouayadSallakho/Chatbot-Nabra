using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Reports;

/// <summary>
/// One request to generate one report, and what became of it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Durable before anything is generated.</strong> A request is written
/// as <see cref="ReportExecutionStatus.Pending"/> and answered with its id; a
/// worker claims it later. A process that dies between the two leaves a row that
/// another instance picks up, not a request that silently vanished.
/// </para>
/// <para>
/// <strong>It records a request, not a query.</strong> A type, a format and a
/// period — nothing a caller could use to widen what is read. There is no SQL,
/// no filter expression and no column list anywhere on this record.
/// </para>
/// <para>
/// Claiming, completion and failure are written by the execution store with
/// conditional SQL, because the question each answers — "is this still mine?" —
/// has to be settled by the database rather than by an object one process
/// happens to hold.
/// </para>
/// </remarks>
public sealed class ReportExecution
{
    private ReportExecution(
        ReportExecutionId id,
        TenantId tenantId,
        ReportType type,
        ReportFormat format,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        ReportExecutionStatus status,
        UserId requestedBy,
        DateTimeOffset requestedAt,
        Guid? clientRequestId,
        ReportScheduleId? scheduleId,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset? failedAt,
        string? failureCode,
        int attempts,
        DateTimeOffset? expiredAt)
    {
        Id = id;
        TenantId = tenantId;
        Type = type;
        Format = format;
        PeriodFrom = periodFrom;
        PeriodTo = periodTo;
        Status = status;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        ClientRequestId = clientRequestId;
        ScheduleId = scheduleId;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        FailedAt = failedAt;
        FailureCode = failureCode;
        Attempts = attempts;
        ExpiredAt = expiredAt;
    }

    public ReportExecutionId Id { get; }

    public TenantId TenantId { get; }

    public ReportType Type { get; }

    public ReportFormat Format { get; }

    public DateTimeOffset PeriodFrom { get; }

    public DateTimeOffset PeriodTo { get; }

    /// <summary>
    /// The zone the period's days were computed in (Phase 37.6).
    /// </summary>
    /// <remarks>
    /// Kept on the execution rather than read from the workspace, so a report keeps
    /// the days it was asked for after the workspace's zone changes. Executions made
    /// before 37.6 read <c>UTC</c>, which is what they were.
    /// </remarks>
    public WorkspaceTimeZone TimeZone { get; private set; } = WorkspaceTimeZone.Utc;

    /// <summary>
    /// The language the report's file is written in (Phase 37.7, M9).
    /// </summary>
    /// <remarks>
    /// Decided when the report is asked for and kept: the request's own language,
    /// else the requester's, else English; a scheduled occurrence takes its
    /// schedule's. Changing a person's language later does not change a report
    /// already asked for. Executions made before 37.7 read English, which is what
    /// they were.
    /// </remarks>
    public UserLanguage Language { get; private set; } = UserLanguage.English;

    public ReportExecutionStatus Status { get; }

    /// <summary>
    /// Whose authority the report is generated under.
    /// </summary>
    /// <remarks>
    /// For a scheduled report, the member who last saved the schedule. Their
    /// permissions are checked again when the report is generated, so a schedule
    /// cannot outlive the authority that created it.
    /// </remarks>
    public UserId RequestedBy { get; }

    public DateTimeOffset RequestedAt { get; }

    /// <summary>The caller's idempotency key. Null for scheduled occurrences.</summary>
    public Guid? ClientRequestId { get; }

    /// <summary>The schedule this occurrence belongs to, if any.</summary>
    public ReportScheduleId? ScheduleId { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public DateTimeOffset? FailedAt { get; }

    /// <summary>A code from <see cref="ReportFailures"/>. Never exception text.</summary>
    public string? FailureCode { get; }

    public int Attempts { get; }

    public DateTimeOffset? ExpiredAt { get; }

    public ReportPeriod Period => ReportPeriod.FromInstants(PeriodFrom, PeriodTo, TimeZone);

    public bool IsTerminal => Status is ReportExecutionStatus.Completed
        or ReportExecutionStatus.Failed
        or ReportExecutionStatus.Expired;

    /// <summary>A report somebody asked for.</summary>
    public static ReportExecution Request(
        TenantId tenantId,
        ReportType type,
        ReportFormat format,
        ReportPeriod period,
        UserId requestedBy,
        Guid clientRequestId,
        DateTimeOffset requestedAt,
        UserLanguage language = UserLanguage.English)
    {
        if (clientRequestId == Guid.Empty)
        {
            throw new DomainValidationException("A report request needs a client request id.")
            {
                Code = "reports.client_request_id.required",
            };
        }

        return New(tenantId, type, format, period, requestedBy, clientRequestId, null, requestedAt, language);
    }

    /// <summary>One occurrence of a schedule.</summary>
    public static ReportExecution ForSchedule(
        ReportSchedule schedule,
        ReportPeriod period,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return New(
            schedule.TenantId,
            schedule.Type,
            schedule.Format,
            period,
            schedule.OwnerUserId,
            null,
            schedule.Id,
            requestedAt,
            schedule.Language);
    }

    private static ReportExecution New(
        TenantId tenantId,
        ReportType type,
        ReportFormat format,
        ReportPeriod period,
        UserId requestedBy,
        Guid? clientRequestId,
        ReportScheduleId? scheduleId,
        DateTimeOffset requestedAt,
        UserLanguage language)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A report belongs to a workspace.")
            {
                Code = "reports.tenant.required",
            };
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException("That report type is not recognised.")
            {
                Code = "reports.type.invalid",
            };
        }

        if (!Enum.IsDefined(format))
        {
            throw new DomainValidationException("That report format is not recognised.")
            {
                Code = "reports.format.invalid",
            };
        }

        if (!Enum.IsDefined(language))
        {
            throw new DomainValidationException("A report is written in English or Arabic.")
            {
                Code = "language.unsupported",
            };
        }

        return new ReportExecution(
            ReportExecutionId.New(),
            tenantId,
            type,
            format,
            period.From,
            period.To,
            ReportExecutionStatus.Pending,
            requestedBy,
            requestedAt,
            clientRequestId,
            scheduleId,
            startedAt: null,
            completedAt: null,
            failedAt: null,
            failureCode: null,
            attempts: 0,
            expiredAt: null)
        {
            TimeZone = period.TimeZone,
            Language = language,
        };
    }

    /// <summary>Whether a repeated request asks for the same report as this one.</summary>
    /// <param name="language">
    /// The language the repeat named, if it named one (Phase 37.7). A repeat that
    /// names none asks for whatever the first request settled on.
    /// </param>
    public bool SameRequestAs(ReportType type, ReportFormat format, ReportPeriod period, UserLanguage? language = null) =>
        Type == type
        && Format == format
        && PeriodFrom == period.From
        && PeriodTo == period.To
        && TimeZone == period.TimeZone
        && (language is null || Language == language);
}
