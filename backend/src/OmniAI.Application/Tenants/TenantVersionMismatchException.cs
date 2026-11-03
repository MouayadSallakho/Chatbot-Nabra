namespace OmniAI.Application.Tenants;

/// <summary>
/// The workspace is not at the version the caller said it was changing
/// (Phase 37.5a).
/// </summary>
/// <remarks>
/// Answered as <c>412 Precondition Failed</c>, like every other stale conditional
/// write: two owners in the same settings screen is the normal case, and the
/// second to save is told rather than silently undoing the first. Nothing is
/// retried, and nothing was written -- neither the name nor its audit record.
/// </remarks>
public sealed class TenantVersionMismatchException : Exception
{
    public TenantVersionMismatchException()
        : base("The workspace has changed since it was read. No change was applied.")
    {
    }

    public TenantVersionMismatchException(string message)
        : base(message)
    {
    }

    public TenantVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static TenantVersionMismatchException ForTenant(Guid tenantId, Exception? innerException = null)
    {
        string message =
            $"The workspace '{tenantId}' has changed since the version you supplied was read. "
            + "No change was applied. Read the workspace again and reapply the change if it is still wanted.";

        return innerException is null
            ? new TenantVersionMismatchException(message)
            : new TenantVersionMismatchException(message, innerException);
    }
}
