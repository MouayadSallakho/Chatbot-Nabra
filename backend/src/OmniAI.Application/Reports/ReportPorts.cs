using OmniAI.Application.Analytics;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>
/// The limits every report is generated within.
/// </summary>
/// <remarks>
/// Deployment settings with safe defaults. Every one of them bounds how much a
/// single request can make this platform read, write or keep.
/// </remarks>
public sealed class ReportOptions
{
    public const string Section = "Reports";

    /// <summary>The most table rows a CSV or XLSX report may carry.</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>The most table rows a PDF may carry. A PDF is read, not processed.</summary>
    public int MaxPdfRows { get; set; } = 2_000;

    /// <summary>The largest file a report may produce.</summary>
    public long MaxArtifactBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>How long a generated file is kept before the retention worker removes it.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>How many schedules one workspace may have.</summary>
    public int MaxSchedulesPerWorkspace { get; set; } = 25;

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);

    public int RowLimitFor(ReportFormat format) => format == ReportFormat.Pdf ? MaxPdfRows : MaxRows;

    public void Validate()
    {
        if (MaxRows is < 1 or > 1_000_000)
        {
            throw new InvalidOperationException($"{Section}:MaxRows must be between 1 and 1000000.");
        }

        if (MaxPdfRows < 1 || MaxPdfRows > MaxRows)
        {
            throw new InvalidOperationException($"{Section}:MaxPdfRows must be between 1 and MaxRows.");
        }

        if (MaxArtifactBytes is < 1024 or > 512L * 1024 * 1024)
        {
            throw new InvalidOperationException($"{Section}:MaxArtifactBytes must be between 1 KiB and 512 MiB.");
        }

        if (RetentionDays is < 1 or > 365)
        {
            throw new InvalidOperationException($"{Section}:RetentionDays must be between 1 and 365.");
        }

        if (MaxSchedulesPerWorkspace is < 1 or > 500)
        {
            throw new InvalidOperationException($"{Section}:MaxSchedulesPerWorkspace must be between 1 and 500.");
        }
    }
}

/// <summary>A file the artifact store has finished writing.</summary>
/// <param name="StorageKey">Opaque and server-generated. Never shown to a client.</param>
/// <param name="Sha256">Lower-case hex of exactly the bytes stored.</param>
public sealed record StoredReportFile(string StorageKey, long ByteLength, string Sha256);

/// <summary>
/// Where generated report files are kept.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The Application knows no path.</strong> A key goes in, a stream comes
/// out. Whether the bytes sit on a disk, in S3 or in a blob container is the
/// implementation's business, and swapping it changes nothing above this line.
/// </para>
/// <para>
/// <strong>A write is all or nothing.</strong> <see cref="WriteAsync"/> hands the
/// caller a stream to fill, and only once the caller has finished, the size has
/// been checked and the hash computed does the file become readable under its
/// key. A failure at any point leaves no readable file behind.
/// </para>
/// </remarks>
public interface IReportArtifactStore
{
    /// <summary>Whether this deployment has somewhere to put reports.</summary>
    bool IsConfigured { get; }

    /// <exception cref="ReportTooLargeException">The content passed <paramref name="maxBytes"/>.</exception>
    /// <exception cref="ReportStorageUnavailableException">No storage is configured.</exception>
    Task<StoredReportFile> WriteAsync(
        Func<Stream, CancellationToken, Task> write,
        long maxBytes,
        CancellationToken cancellationToken = default);

    /// <returns>The file, or <see langword="null"/> when nothing is stored under the key.</returns>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes a file. Removing one that is already gone is not an error.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes unfinished writes a crashed process left behind.</summary>
    Task<int> SweepAbandonedWritesAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keys of finished files last written before <paramref name="finalizedBefore"/>,
    /// in key order after <paramref name="afterKey"/>, at most <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    /// Only names of the store's own key shape are ever returned: nothing else in
    /// the storage root is a report, and nothing else is ever a deletion candidate.
    /// </remarks>
    Task<IReadOnlyList<string>> ListFinalizedAsync(
        DateTimeOffset finalizedBefore,
        string? afterKey,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a finished file exists under the key.</summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Writes a report document as one file format.</summary>
public interface IReportRenderer
{
    ReportFormat Format { get; }

    Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken = default);
}

/// <summary>One canonical customer's activity in a period.</summary>
public sealed record CustomerActivityRow(
    CustomerId CustomerId,
    CustomerDisplayName? DisplayName,
    IReadOnlyList<ChannelKind> Channels,
    long ConversationsActive,
    long ConversationsStarted,
    long CustomerMessages,
    DateTimeOffset? FirstActivityAt,
    DateTimeOffset? LastActivityAt);

/// <summary>
/// Reads customer activity over the same base sets the analytics dashboard counts.
/// </summary>
public interface ICustomerActivityReader
{
    /// <summary>
    /// Active customers, last activity first, then by id. At most
    /// <paramref name="limit"/> rows.
    /// </summary>
    Task<IReadOnlyList<CustomerActivityRow>> ReadAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>An execution as a list shows it.</summary>
public sealed record ReportExecutionRecord(
    ReportExecution Execution,
    string? ScheduleName,
    long? ByteLength,
    DateTimeOffset? ExpiresAt);

/// <summary>An execution a worker has claimed, with the token that proves it.</summary>
public sealed record ClaimedReportExecution(
    ReportExecutionId Id,
    TenantId TenantId,
    ReportType Type,
    ReportFormat Format,
    ReportPeriod Period,
    UserId RequestedBy,
    Guid ClaimToken,
    int Attempts);

/// <summary>Report executions and their artifacts, in the database.</summary>
public interface IReportExecutionStore
{
    /// <summary>
    /// Records a request, or returns the one already recorded under the same key.
    /// </summary>
    /// <remarks>
    /// The key is (workspace, requester, client request id), enforced by a unique
    /// index — so a double-click is decided by the database, not by a button.
    /// </remarks>
    Task<(ReportExecution Execution, bool Created)> AddOrGetAsync(
        ReportExecution execution,
        CancellationToken cancellationToken = default);

    Task<ReportExecutionRecord?> FindAsync(
        TenantId tenantId,
        ReportExecutionId id,
        CancellationToken cancellationToken = default);

    /// <summary>Newest first, then by id.</summary>
    Task<(IReadOnlyList<ReportExecutionRecord> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ReportArtifact?> FindArtifactAsync(
        TenantId tenantId,
        ReportExecutionId executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims pending executions, and running ones whose lease has lapsed.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c>, so several instances take disjoint work.
    /// Each claim carries a fresh token; only its holder can settle the execution.
    /// Executions that have run out of attempts are failed here rather than
    /// claimed again.
    /// </remarks>
    Task<IReadOnlyList<ClaimedReportExecution>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the artifact and completes the execution, in one transaction.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the claim is no longer this caller's — the
    /// lease lapsed and another worker took over — in which case nothing is
    /// written and the caller must discard its file.
    /// </returns>
    Task<bool> CompleteAsync(
        ReportExecutionId id,
        Guid claimToken,
        ReportArtifact artifact,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> FailAsync(
        ReportExecutionId id,
        Guid claimToken,
        string failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Hands a claimed execution back after a transient failure.</summary>
    Task<bool> ReleaseAsync(
        ReportExecutionId id,
        Guid claimToken,
        CancellationToken cancellationToken = default);

    /// <summary>Artifacts whose retention has ended.</summary>
    Task<IReadOnlyList<ReportArtifact>> ListExpiredArtifactsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Which of these storage keys an artifact row still references.</summary>
    Task<IReadOnlySet<string>> ReferencedKeysAsync(
        IReadOnlyCollection<string> storageKeys,
        CancellationToken cancellationToken = default);

    /// <summary>Artifact rows in id order after <paramref name="afterId"/>, for verifying their files exist.</summary>
    Task<IReadOnlyList<ReportArtifact>> ListArtifactsAsync(
        Guid? afterId,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an artifact's row and marks its execution expired. Idempotent.</summary>
    Task MarkExpiredAsync(
        ReportArtifact artifact,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>A schedule as a list shows it.</summary>
public sealed record ReportScheduleRecord(
    Versioned<ReportSchedule> Schedule,
    ReportExecution? LastExecution);

/// <summary>
/// Due schedules, locked for the length of one transaction.
/// </summary>
/// <remarks>
/// Every schedule in <see cref="Schedules"/> is row-locked until
/// <see cref="CommitAsync"/> or disposal, so another instance skips it. Creating
/// the occurrence and advancing the schedule commit together: a crash between the
/// two is not possible, and a repeat of the same occurrence is refused by its
/// unique key.
/// </remarks>
public interface IDueReportScheduleBatch : IAsyncDisposable
{
    IReadOnlyList<ReportSchedule> Schedules { get; }

    /// <summary>
    /// The zone of the schedule's workspace, read with the batch (Phase 37.6);
    /// UTC if the workspace is gone.
    /// </summary>
    WorkspaceTimeZone TimeZoneOf(ReportSchedule schedule);

    /// <summary>Queues an occurrence (once per period) and moves the schedule on.</summary>
    /// <returns><see langword="false"/> when this occurrence already existed.</returns>
    Task<bool> EnqueueAsync(
        ReportSchedule schedule,
        ReportExecution occurrence,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken = default);

    /// <summary>Switches a schedule off on the platform's authority, with a reason.</summary>
    Task DisableAsync(
        ReportSchedule schedule,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Report schedules, in the database.</summary>
public interface IReportScheduleStore
{
    Task<ConcurrencyToken> AddAsync(ReportSchedule schedule, CancellationToken cancellationToken = default);

    Task<Versioned<ReportSchedule>?> FindAsync(
        TenantId tenantId,
        ReportScheduleId id,
        CancellationToken cancellationToken = default);

    /// <summary>Every schedule of one workspace, by name then id. Bounded by the per-workspace limit.</summary>
    Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <exception cref="ReportScheduleVersionMismatchException">The stored row is not at <paramref name="expected"/>.</exception>
    Task<ConcurrencyToken> UpdateAsync(
        ReportSchedule schedule,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>Locks up to <paramref name="batchSize"/> enabled schedules that are due.</summary>
    Task<IDueReportScheduleBatch> ClaimDueAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>The report grew past what this deployment exports.</summary>
public sealed class ReportTooLargeException : Exception
{
    public ReportTooLargeException()
        : base("Report exceeds the supported export size.")
    {
    }

    public ReportTooLargeException(string message)
        : base(message)
    {
    }

    public ReportTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>This deployment has no report storage configured.</summary>
public sealed class ReportStorageUnavailableException : Exception
{
    public ReportStorageUnavailableException()
        : base("Report storage is not configured on this deployment.")
    {
    }

    public ReportStorageUnavailableException(string message)
        : base(message)
    {
    }

    public ReportStorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A client request id was reused for a different report.</summary>
public sealed class ReportRequestConflictException : Exception
{
    public ReportRequestConflictException()
        : base("That request id was already used for a different report. Use a new one.")
    {
    }

    public ReportRequestConflictException(string message)
        : base(message)
    {
    }

    public ReportRequestConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The report exists but has no file to download, and says why.</summary>
public sealed class ReportNotDownloadableException : Exception
{
    public ReportNotDownloadableException()
        : this(ReportExecutionStatus.Pending)
    {
    }

    public ReportNotDownloadableException(ReportExecutionStatus status)
        : base(status switch
        {
            ReportExecutionStatus.Failed => "This report failed, so there is no file to download.",
            ReportExecutionStatus.Expired =>
                "This report's file was removed by the retention policy. Generate it again.",
            _ => "This report has not finished generating yet.",
        })
    {
        Status = status;
    }

    public ReportNotDownloadableException(string message)
        : base(message)
    {
    }

    public ReportNotDownloadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private ReportNotDownloadableException(ReportExecutionStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    public ReportExecutionStatus Status { get; }

    /// <summary>
    /// The record says the file exists, and the store does not have it.
    /// </summary>
    /// <remarks>
    /// Said as what it is, not blamed on retention: storage that was lost or
    /// replaced is an operational fact an administrator needs to hear about.
    /// </remarks>
    public static ReportNotDownloadableException Missing() =>
        new(
            ReportExecutionStatus.Expired,
            "This report's file is no longer in report storage. Generate the report again.");
}

/// <summary>A schedule was changed since the caller read it.</summary>
public sealed class ReportScheduleVersionMismatchException : Exception
{
    public ReportScheduleVersionMismatchException()
        : base("This schedule was changed since you opened it. Reload it and try again.")
    {
    }

    public ReportScheduleVersionMismatchException(string message)
        : base(message)
    {
    }

    public ReportScheduleVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The workspace already has as many schedules as it may.</summary>
public sealed class ReportScheduleLimitException : Exception
{
    public ReportScheduleLimitException()
        : base("This workspace already has the maximum number of report schedules.")
    {
    }

    public ReportScheduleLimitException(string message)
        : base(message)
    {
    }

    public ReportScheduleLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
