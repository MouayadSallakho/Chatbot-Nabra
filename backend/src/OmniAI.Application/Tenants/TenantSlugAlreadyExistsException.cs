namespace OmniAI.Application.Tenants;

/// <summary>
/// Raised when a tenant cannot be created because its slug is already taken.
/// </summary>
/// <remarks>
/// The one error type this phase adds, and it exists because the outcome is
/// neither invalid input nor a server fault: the request was well-formed and the
/// rule it broke depends on data the caller cannot see.
/// <para>
/// It is transport-neutral by construction. There is no status code, no
/// <c>ProblemDetails</c>, no header and no HTTP vocabulary here; the API decides
/// how to surface it. It is also provider-neutral: Infrastructure translates a
/// PostgreSQL unique violation into this type, so nothing above Infrastructure
/// ever sees an <c>Npgsql</c> exception, a SQLSTATE or a constraint name.
/// </para>
/// <para>
/// Deliberately not accompanied by a <c>TenantNotFoundException</c>. A query that
/// finds nothing returns <see langword="null"/>, which is an ordinary answer
/// rather than a failure and needs no type to express it. One focused exception,
/// not a catalogue.
/// </para>
/// </remarks>
public sealed class TenantSlugAlreadyExistsException : Exception
{
    public TenantSlugAlreadyExistsException()
        : base("A tenant with the requested slug already exists.")
    {
    }

    public TenantSlugAlreadyExistsException(string message)
        : base(message)
    {
    }

    public TenantSlugAlreadyExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The slug that was rejected, when the thrower knew it.
    /// </summary>
    /// <remarks>
    /// Carried so the API can name the conflicting value without re-parsing a
    /// message. It is caller-supplied input, never a credential.
    /// </remarks>
    public string? Slug { get; private init; }

    /// <summary>Creates the exception for a known slug.</summary>
    public static TenantSlugAlreadyExistsException ForSlug(string slug, Exception? innerException = null)
    {
        string message = $"A tenant with the slug '{slug}' already exists.";

        return innerException is null
            ? new TenantSlugAlreadyExistsException(message) { Slug = slug }
            : new TenantSlugAlreadyExistsException(message, innerException) { Slug = slug };
    }
}
