using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Reports;

/// <summary>
/// A report generated automatically for each completed week or month.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bounded on purpose.</strong> Two frequencies, one fixed range per
/// frequency, no cron, no hourly option, no recipients. A schedule produces a
/// report that appears in the workspace's Reports list and does nothing else:
/// there is no email, no webhook and no external delivery in this phase.
/// </para>
/// <para>
/// <strong>It runs under a person's authority.</strong>
/// <see cref="OwnerUserId"/> is whoever last saved it, and their permissions are
/// checked again at every occurrence. A schedule whose owner has lost those
/// permissions is switched off rather than left generating sensitive data under
/// authority nobody holds any more.
/// </para>
/// </remarks>
public sealed class ReportSchedule
{
    /// <summary>The longest name a schedule may have.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Why the platform, rather than a person, switched a schedule off.</summary>
    public const string OwnerLostPermission = "owner_lost_permission";

    private ReportSchedule(
        ReportScheduleId id,
        TenantId tenantId,
        string name,
        ReportType type,
        ReportFormat format,
        ReportFrequency frequency,
        bool enabled,
        DateTimeOffset nextRunAt,
        UserId ownerUserId,
        UserId createdBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? disabledReason)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Type = type;
        Format = format;
        Frequency = frequency;
        Enabled = enabled;
        NextRunAt = nextRunAt;
        OwnerUserId = ownerUserId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DisabledReason = disabledReason;
    }

    public ReportScheduleId Id { get; }

    public TenantId TenantId { get; }

    /// <summary>A workspace-authored label. Untrusted text; rendered escaped.</summary>
    public string Name { get; private set; }

    public ReportType Type { get; private set; }

    public ReportFormat Format { get; private set; }

    public ReportFrequency Frequency { get; private set; }

    public bool Enabled { get; private set; }

    /// <summary>When the next occurrence becomes due: always a calendar boundary.</summary>
    public DateTimeOffset NextRunAt { get; private set; }

    /// <summary>The member whose permissions each occurrence is generated under.</summary>
    public UserId OwnerUserId { get; private set; }

    public UserId CreatedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set when the platform switched the schedule off, and why.</summary>
    public string? DisabledReason { get; private set; }

    public static ReportSchedule Create(
        TenantId tenantId,
        string? name,
        ReportType type,
        ReportFormat format,
        ReportFrequency frequency,
        bool enabled,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A schedule belongs to a workspace.");
        }

        Validate(type, format, frequency);

        return new ReportSchedule(
            ReportScheduleId.New(),
            tenantId,
            NormaliseName(name),
            type,
            format,
            frequency,
            enabled,
            ReportScheduleCalendar.NextBoundary(frequency, now),
            createdBy,
            createdBy,
            now,
            now,
            disabledReason: null);
    }

    /// <summary>
    /// Replaces everything a person may edit, and makes them its owner.
    /// </summary>
    /// <remarks>
    /// The next run is recalculated from now: changing a weekly schedule to a
    /// monthly one must not leave it due next Monday.
    /// </remarks>
    public void Update(
        string? name,
        ReportType type,
        ReportFormat format,
        ReportFrequency frequency,
        bool enabled,
        UserId editor,
        DateTimeOffset now)
    {
        Validate(type, format, frequency);

        bool rescheduled = frequency != Frequency || (enabled && !Enabled);

        Name = NormaliseName(name);
        Type = type;
        Format = format;
        Frequency = frequency;
        Enabled = enabled;
        OwnerUserId = editor;
        UpdatedAt = now;

        if (enabled)
        {
            DisabledReason = null;
        }

        if (rescheduled)
        {
            NextRunAt = ReportScheduleCalendar.NextBoundary(frequency, now);
        }
    }

    /// <summary>Switches the schedule on or off, and makes the editor its owner.</summary>
    public void SetEnabled(bool enabled, UserId editor, DateTimeOffset now)
    {
        if (enabled && !Enabled)
        {
            NextRunAt = ReportScheduleCalendar.NextBoundary(Frequency, now);
            DisabledReason = null;
        }

        Enabled = enabled;
        OwnerUserId = editor;
        UpdatedAt = now;
    }

    private static void Validate(ReportType type, ReportFormat format, ReportFrequency frequency)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException("That report type is not recognised.");
        }

        if (!Enum.IsDefined(format))
        {
            throw new DomainValidationException("That report format is not recognised.");
        }

        if (!Enum.IsDefined(frequency))
        {
            throw new DomainValidationException("A schedule runs weekly or monthly.");
        }
    }

    private static string NormaliseName(string? name)
    {
        string trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("A schedule needs a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainValidationException(
                $"A schedule name may be at most {MaxNameLength} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new DomainValidationException("A schedule name may not contain control characters.");
        }

        return trimmed;
    }
}
