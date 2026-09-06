using OmniAI.Domain.Users;

namespace OmniAI.Application.Security;

/// <summary>
/// Who is making the current request.
/// </summary>
/// <remarks>
/// <strong>The whole point of this interface is what it does not mention.</strong>
/// There is no <c>ClaimsPrincipal</c>, no <c>HttpContext</c>, no cookie, no
/// bearer token, no scheme name and no claim type. A use case needs to know two
/// things -- whether somebody is authenticated, and who they are -- and neither
/// of those answers depends on how the answer arrived. An executable guard
/// asserts that the Application assembly references no ASP.NET Core assembly at
/// all, so this cannot quietly acquire an HTTP dependency later.
/// <para>
/// The implementation lives in the API, where reading a
/// <c>ClaimsPrincipal</c> is exactly the right thing to do. If this platform ever
/// authenticates a background job, a queue consumer or a service-to-service call,
/// that host supplies its own implementation and no use case changes.
/// </para>
/// <para>
/// <strong>It answers about the caller, never about permissions.</strong> "May
/// this person see this tenant" is a different question with a different answer
/// source -- a membership row in the database -- and it is asked through
/// <see cref="Tenants.TenantAccessGuard"/>. Keeping the two apart means a
/// forged or malformed principal cannot become authorisation, because being
/// authenticated grants nothing on its own.
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>Whether the request carries a verified identity.</summary>
    /// <remarks>
    /// True only when an identity was actually established and carried a usable
    /// user id. A principal that is marked authenticated but whose id claim is
    /// missing or malformed is not authenticated for the purposes of this
    /// platform: it cannot be matched to a membership row, so treating it as
    /// authenticated would only produce a confusing failure further down.
    /// </remarks>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The authenticated user's identity, or <see langword="null"/> when the
    /// request is anonymous.
    /// </summary>
    /// <remarks>
    /// Nullable rather than throwing, and rather than returning a default
    /// <see cref="Domain.Users.UserId"/>. A default id would compare equal to
    /// every other default id, which is precisely the value that must never be
    /// allowed to reach a membership lookup.
    /// </remarks>
    UserId? Id { get; }
}
