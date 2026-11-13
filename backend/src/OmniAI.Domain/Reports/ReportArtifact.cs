using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Reports;

/// <summary>
/// What is known about one generated file. Never the file itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The bytes live in the artifact store; this row describes them.</strong>
/// A report can run to megabytes, and PostgreSQL is the wrong place to keep a
/// file whose only use is to be streamed back once or twice and then deleted.
/// </para>
/// <para>
/// <strong>The storage key grants nothing.</strong> It is an opaque value the
/// server generated, it never leaves the server, and reading the file it names
/// still requires a download request that proves workspace membership and the
/// report's permissions.
/// </para>
/// </remarks>
public sealed class ReportArtifact
{
    private ReportArtifact(
        ReportArtifactId id,
        TenantId tenantId,
        ReportExecutionId executionId,
        ReportFormat format,
        string contentType,
        long byteLength,
        string sha256,
        string storageKey,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        ExecutionId = executionId;
        Format = format;
        ContentType = contentType;
        ByteLength = byteLength;
        Sha256 = sha256;
        StorageKey = storageKey;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public ReportArtifactId Id { get; }

    public TenantId TenantId { get; }

    public ReportExecutionId ExecutionId { get; }

    public ReportFormat Format { get; }

    public string ContentType { get; }

    public long ByteLength { get; }

    /// <summary>Lower-case hex.</summary>
    public string Sha256 { get; }

    public string StorageKey { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the retention policy removes the file.</summary>
    public DateTimeOffset ExpiresAt { get; }

    public static ReportArtifact Create(
        TenantId tenantId,
        ReportExecutionId executionId,
        ReportFormat format,
        string contentType,
        long byteLength,
        string sha256,
        string storageKey,
        DateTimeOffset createdAt,
        TimeSpan retention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (byteLength <= 0)
        {
            throw new DomainValidationException("A report file cannot be empty.")
            {
                Code = "reports.artifact.invalid",
            };
        }

        if (retention <= TimeSpan.Zero)
        {
            throw new DomainValidationException("A report must be kept for a positive period.")
            {
                Code = "reports.artifact.invalid",
            };
        }

        return new ReportArtifact(
            ReportArtifactId.New(),
            tenantId,
            executionId,
            format,
            contentType,
            byteLength,
            sha256,
            storageKey,
            createdAt,
            createdAt + retention);
    }
}
