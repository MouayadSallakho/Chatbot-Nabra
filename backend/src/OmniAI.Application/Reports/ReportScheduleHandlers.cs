using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>A schedule as the API publishes it.</summary>
public sealed record ReportScheduleView(
    Guid Id,
    string Name,
    ReportType Type,
    string Title,
    ReportFormat Format,
    ReportFrequency Frequency,
    bool Enabled,
    DateTimeOffset NextRunAt,
    bool OwnedByMe,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? DisabledReason,
    ReportExecutionView? LastExecution,
    bool CanEdit);

/// <summary>Create a schedule, or replace one's editable fields.</summary>
public sealed record SaveReportScheduleCommand(
    Guid TenantId,
    Guid? ScheduleId,
    string? Name,
    string? ReportType,
    string? Format,
    string? Frequency,
    bool? Enabled,
    ConcurrencyToken Expected);

/// <summary>Switch a schedule on or off.</summary>
public sealed record SetReportScheduleEnabledCommand(
    Guid TenantId,
    Guid ScheduleId,
    bool Enabled,
    ConcurrencyToken Expected);

/// <summary>
/// Reading and administering report schedules.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Saving a schedule is a statement of authority.</strong> The editor
/// becomes its owner, and must hold <c>reports.schedule</c>,
/// <c>reports.generate</c> and the report's data permission at the moment they
/// save it. Every occurrence then re-checks the owner; see
/// <see cref="ReportScheduleRunner"/>.
/// </para>
/// <para>
/// <strong>Optimistic concurrency, no retry.</strong> Every change presents the
/// version it was made against. A stale one is refused, and the person re-reads —
/// the platform never merges two people's edits to a schedule on their behalf.
/// </para>
/// </remarks>
public sealed class ReportScheduleHandlers(
    TenantAuthorizer authorizer,
    IReportScheduleStore schedules,
    ReportOptions options,
    TimeProvider clock)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IReportScheduleStore _schedules =
        schedules ?? throw new ArgumentNullException(nameof(schedules));

    private readonly ReportOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IReadOnlyList<ReportScheduleView>?> ListAsync(
        Guid tenantId,
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

        IReadOnlyList<ReportScheduleRecord> records = await _schedules
            .ListAsync(workspace, cancellationToken)
            .ConfigureAwait(false);

        UserId me = _authorizer.RequireCurrentUser();
        DateTimeOffset now = _clock.GetUtcNow();

        return [.. records.Select(record => View(record, held, me, now))];
    }

    public async Task<Versioned<ReportScheduleView>?> GetAsync(
        Guid tenantId,
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        TenantId workspace = TenantId.From(tenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsView, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || scheduleId == Guid.Empty)
        {
            return null;
        }

        return await ReadAsync(workspace, ReportScheduleId.From(scheduleId), held, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <exception cref="ReportScheduleLimitException">The workspace is at its schedule limit.</exception>
    public async Task<Versioned<ReportScheduleView>?> CreateAsync(
        SaveReportScheduleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId workspace = TenantId.From(command.TenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsSchedule, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return null;
        }

        (ReportType type, ReportFormat format, ReportFrequency frequency) = Parse(command, held);

        if (await _schedules.CountAsync(workspace, cancellationToken).ConfigureAwait(false)
            >= _options.MaxSchedulesPerWorkspace)
        {
            throw new ReportScheduleLimitException();
        }

        ReportSchedule schedule = ReportSchedule.Create(
            workspace,
            command.Name,
            type,
            format,
            frequency,
            command.Enabled ?? true,
            _authorizer.RequireCurrentUser(),
            _clock.GetUtcNow());

        await _schedules.AddAsync(schedule, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(workspace, schedule.Id, held, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="ReportScheduleVersionMismatchException">The schedule changed since it was read.</exception>
    public async Task<Versioned<ReportScheduleView>?> UpdateAsync(
        SaveReportScheduleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId workspace = TenantId.From(command.TenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsSchedule, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || command.ScheduleId is not Guid id || id == Guid.Empty)
        {
            return null;
        }

        Versioned<ReportSchedule>? stored = await _schedules
            .FindAsync(workspace, ReportScheduleId.From(id), cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // The report being edited away from must be one the editor may schedule
        // too; otherwise somebody without customers.view could repoint a customer
        // schedule and take ownership of it without ever holding its authority.
        ReportHandlers.RequireAll(held, ReportCatalog.ToSchedule(stored.Value.Type));

        (ReportType type, ReportFormat format, ReportFrequency frequency) = Parse(command, held);

        stored.Value.Update(
            command.Name,
            type,
            format,
            frequency,
            command.Enabled ?? stored.Value.Enabled,
            _authorizer.RequireCurrentUser(),
            _clock.GetUtcNow());

        await _schedules.UpdateAsync(stored.Value, command.Expected, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(workspace, stored.Value.Id, held, cancellationToken).ConfigureAwait(false);
    }

    /// <exception cref="ReportScheduleVersionMismatchException">The schedule changed since it was read.</exception>
    public async Task<Versioned<ReportScheduleView>?> SetEnabledAsync(
        SetReportScheduleEnabledCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId workspace = TenantId.From(command.TenantId);

        IReadOnlyList<PermissionCode>? held = await RequireAsync(
                workspace, PlatformPermissions.ReportsSchedule, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || command.ScheduleId == Guid.Empty)
        {
            return null;
        }

        Versioned<ReportSchedule>? stored = await _schedules
            .FindAsync(workspace, ReportScheduleId.From(command.ScheduleId), cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Switching one off needs only reports.schedule: stopping a report is
        // never a way to read anything. Switching one on makes the caller its
        // owner, so it needs everything the report does.
        if (command.Enabled)
        {
            ReportHandlers.RequireAll(held, ReportCatalog.ToSchedule(stored.Value.Type));
        }

        stored.Value.SetEnabled(command.Enabled, _authorizer.RequireCurrentUser(), _clock.GetUtcNow());

        await _schedules.UpdateAsync(stored.Value, command.Expected, cancellationToken).ConfigureAwait(false);

        return await ReadAsync(workspace, stored.Value.Id, held, cancellationToken).ConfigureAwait(false);
    }

    private static (ReportType Type, ReportFormat Format, ReportFrequency Frequency) Parse(
        SaveReportScheduleCommand command,
        IReadOnlyList<PermissionCode> held)
    {
        ReportType type = ReportRequestParsing.Type(command.ReportType);
        ReportFormat format = ReportRequestParsing.Format(command.Format);
        ReportFrequency frequency = ReportRequestParsing.Frequency(command.Frequency);

        ReportHandlers.RequireAll(held, ReportCatalog.ToSchedule(type));

        if (!ReportCatalog.For(type).Supports(format))
        {
            throw new DomainValidationException("That report is not available in that format.");
        }

        return (type, format, frequency);
    }

    private async Task<Versioned<ReportScheduleView>?> ReadAsync(
        TenantId workspace,
        ReportScheduleId id,
        IReadOnlyList<PermissionCode> held,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportScheduleRecord> all = await _schedules
            .ListAsync(workspace, cancellationToken)
            .ConfigureAwait(false);

        ReportScheduleRecord? record = all.FirstOrDefault(candidate => candidate.Schedule.Value.Id == id);

        return record is null
            ? null
            : new Versioned<ReportScheduleView>(
                View(record, held, _authorizer.RequireCurrentUser(), _clock.GetUtcNow()),
                record.Schedule.Version);
    }

    private static ReportScheduleView View(
        ReportScheduleRecord record,
        IReadOnlyCollection<PermissionCode> held,
        UserId viewer,
        DateTimeOffset now)
    {
        ReportSchedule schedule = record.Schedule.Value;

        return new ReportScheduleView(
            schedule.Id.Value,
            schedule.Name,
            schedule.Type,
            ReportCatalog.For(schedule.Type).Title,
            schedule.Format,
            schedule.Frequency,
            schedule.Enabled,
            schedule.NextRunAt,
            schedule.OwnerUserId == viewer,
            schedule.CreatedAt,
            schedule.UpdatedAt,
            schedule.DisabledReason,
            record.LastExecution is ReportExecution last
                ? ReportHandlers.View(new ReportExecutionRecord(last, schedule.Name, null, null), held, viewer, now)
                : null,
            ReportCatalog.Holds(held, ReportCatalog.ToSchedule(schedule.Type)));
    }

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
}
