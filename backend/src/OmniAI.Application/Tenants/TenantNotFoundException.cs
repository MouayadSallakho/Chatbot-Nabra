namespace OmniAI.Application.Tenants;

/// <summary>
/// Raised when an operation names a tenant that does not exist.
/// </summary>
/// <remarks>
/// Phase 5 deliberately had no such type, and that decision was about queries: a
/// lookup that finds nothing returns <see langword="null"/>, which is an ordinary
/// answer rather than a failure. This type is for the other situation. Creating
/// an AI agent under a tenant is a <em>command</em> whose precondition failed --
/// the caller asked for work to be done somewhere that does not exist -- and a
/// command has no "no result" to return in place of the thing it was asked to
/// make. One focused exception for a genuinely different outcome, not a
/// catalogue.
/// <para>
/// Transport-neutral by construction: no status code, no <c>ProblemDetails</c>,
/// no HTTP vocabulary. It is also provider-neutral -- Infrastructure translates a
/// PostgreSQL foreign key violation into this type, so nothing above
/// Infrastructure ever sees an <c>Npgsql</c> exception, a SQLSTATE or a
/// constraint name.
/// </para>
/// </remarks>
public sealed class TenantNotFoundException : Exception
{
    public TenantNotFoundException()
        : base("The requested tenant does not exist.")
    {
    }

    public TenantNotFoundException(string message)
        : base(message)
    {
    }

    public TenantNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The tenant that was not found, when the thrower knew it.
    /// </summary>
    /// <remarks>
    /// An identifier the caller already supplied in the route, never a credential.
    /// </remarks>
    public Guid? TenantId { get; private init; }

    /// <summary>Creates the exception for a known tenant id.</summary>
    public static TenantNotFoundException ForId(Guid tenantId, Exception? innerException = null)
    {
        string message = $"No tenant exists with id '{tenantId}'.";

        return innerException is null
            ? new TenantNotFoundException(message) { TenantId = tenantId }
            : new TenantNotFoundException(message, innerException) { TenantId = tenantId };
    }
}
