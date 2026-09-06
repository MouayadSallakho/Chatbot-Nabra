namespace OmniAI.Application.Authorization;

/// <summary>
/// Raised when a conditional write names a version of a role that is no longer
/// the current one.
/// </summary>
/// <remarks>
/// <para>
/// Covers both ways that can happen, because to the caller they are the same
/// answer -- "what you were looking at is out of date": the version was already
/// superseded before the request arrived, or a transaction committed while this
/// one was running. The first is the common case, and it is the reason roles need
/// this at all: a permission editor is a screen somebody leaves open while they
/// think about who should be able to do what.
/// </para>
/// <para>
/// <strong>Distinct from the two conflicts a role can also produce, and the
/// distinctions are the point.</strong> A name collision means the caller must
/// choose a different name; a protected-role refusal means the operation will
/// never be allowed on that role; this means the operation is very likely fine
/// and the caller simply has not seen the role as it stands. Only the last is
/// fixed by re-reading, so only the last is a precondition failure --
/// <c>412</c> -- and reporting the others the same way would tell somebody to
/// refresh a screen that refreshing cannot help.
/// </para>
/// <para>
/// <strong>Evaluated before the protected-role rule.</strong> A stale write
/// against the Owner role is reported as stale, not as protected. The caller's
/// precondition genuinely failed, and answering the protected rule first would
/// describe a role they have not seen -- while also confirming, to somebody
/// holding an out-of-date version, exactly which role they had hold of.
/// </para>
/// <para>
/// Transport-neutral and provider-neutral by construction: no status code, no
/// <c>ProblemDetails</c>, no <c>ETag</c>, no EF Core type, Npgsql type, SQLSTATE
/// or SQL text. Infrastructure translates the database's answer into this before
/// it crosses the boundary.
/// </para>
/// <para>
/// <strong>Nothing retries automatically.</strong> Reapplying a permission set
/// computed against state the caller has never seen is exactly the lost update
/// this mechanism exists to prevent -- and with permissions the loss is authority
/// somebody deliberately removed, quietly restored. The caller is told, and
/// decides.
/// </para>
/// </remarks>
public sealed class TenantRoleVersionMismatchException : Exception
{
    public TenantRoleVersionMismatchException()
        : base("The role has changed since it was read. No change was applied.")
    {
    }

    public TenantRoleVersionMismatchException(string message)
        : base(message)
    {
    }

    public TenantRoleVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The role that could not be updated, when the thrower knew it.</summary>
    public Guid? TenantRoleId { get; private init; }

    /// <summary>Creates the exception for a known role.</summary>
    public static TenantRoleVersionMismatchException ForRole(
        Guid tenantRoleId,
        Exception? innerException = null)
    {
        string message =
            $"The role '{tenantRoleId}' has changed since the version you supplied was read. " +
            "No change was applied. Read the role again and reapply the change if it is still wanted.";

        return innerException is null
            ? new TenantRoleVersionMismatchException(message) { TenantRoleId = tenantRoleId }
            : new TenantRoleVersionMismatchException(message, innerException)
            {
                TenantRoleId = tenantRoleId,
            };
    }
}
