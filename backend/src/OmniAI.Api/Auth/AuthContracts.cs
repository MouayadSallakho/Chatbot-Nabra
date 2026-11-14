using System.Text.Json.Serialization;

namespace OmniAI.Api.Auth;

/// <summary>
/// Request body for <c>POST /api/auth/register</c>.
/// </summary>
/// <param name="Email">The address that will be the login identifier.</param>
/// <param name="Password">The chosen password, in the clear over TLS and never stored as given.</param>
/// <remarks>
/// <strong>Two fields, and the omissions are the point.</strong> There is no
/// tenant id, no role, no permission list and no administrator flag. Registration
/// creates an account and nothing else: a caller cannot name the workspace they
/// join, because joining a workspace is not something registration does, and a
/// caller cannot ask to be an administrator, because a field that grants
/// privilege from a request body is a privilege-escalation endpoint with extra
/// steps.
/// <para>
/// Both properties are nullable and carry no validation attributes. Identity owns
/// the password policy and the email format rules, and a <c>[Required]</c> here
/// would be a second copy of them that can disagree with the first.
/// </para>
/// </remarks>
public sealed record RegisterRequest(string? Email, string? Password, string? Language = null);

/// <summary>
/// Request body for <c>POST /api/auth/login</c>.
/// </summary>
/// <param name="Email">The login identifier.</param>
/// <param name="Password">The password to verify.</param>
/// <remarks>
/// No "remember me". Persisting a sign-in across browser restarts is a decision
/// about how long a stolen device stays useful, and it is not one to make in
/// passing: the session cookie expires when the browser closes, deliberately.
/// </remarks>
public sealed record LoginRequest(string? Email, string? Password, string? Language = null);

/// <summary>
/// The authenticated account, as <c>GET /api/auth/me</c> publishes it.
/// </summary>
/// <param name="Id">The account's identity.</param>
/// <param name="Email">The address it signs in with.</param>
/// <param name="Language">The stored language, or null (Phase 37.3).</param>
/// <param name="DisplayName">
/// The name colleagues see, or null when the person has not given one (Phase 39.1).
/// The email stays: older clients read it, and it is the fallback label.
/// </param>
/// <remarks>
/// <strong>Two fields, chosen by subtraction.</strong> The stored account also
/// has a password hash, a security stamp, a concurrency stamp, a lockout end, a
/// failed-attempt count, a two-factor flag and confirmation state. None of them
/// appears here. The hash and the security stamp are credentials; the lockout
/// fields would tell whoever holds the session how close somebody is to being
/// locked out; the rest describe features that do not exist yet. This type is
/// written out by hand rather than serialised from the Identity entity precisely
/// so that adding a column to that entity cannot quietly add a field here.
/// </remarks>
public sealed record CurrentUserResponse(Guid Id, string Email, string? Language, string? DisplayName);

/// <summary>
/// <c>GET /api/auth/session</c> (Phase 37.5b, row 29): whether this request carries
/// a good session, and whose.
/// </summary>
/// <param name="Authenticated">
/// <see langword="false"/> when signed out, and when the cookie's session was
/// revoked, has expired or names an account that is gone.
/// </param>
/// <param name="User">
/// The same three facts <c>GET /api/auth/me</c> answers with, and nothing more.
/// Left out entirely when not authenticated, so a signed-out body is exactly
/// <c>{"authenticated":false}</c>.
/// </param>
public sealed record SessionResponse(
    bool Authenticated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CurrentUserResponse? User)
{
    /// <summary>The answer for every caller without a good session.</summary>
    public static readonly SessionResponse SignedOut = new(false, null);
}

/// <summary><c>POST /api/auth/password/forgot</c> (Phase 37.3).</summary>
public sealed record ForgotPasswordRequest(string? Email);

/// <summary><c>POST /api/auth/password/reset</c> (Phase 37.3): the two halves of the link, and the new password.</summary>
public sealed record ResetPasswordRequest(string? UserId, string? Token, string? NewPassword);

/// <summary><c>PUT /api/me/language</c> (Phase 37.3).</summary>
public sealed record ChangeLanguageRequest(string? Language);

/// <summary><c>PUT /api/me/display-name</c> (Phase 39.1).</summary>
/// <param name="DisplayName">The new name; null, empty or blank clears it.</param>
public sealed record ChangeDisplayNameRequest(string? DisplayName);

/// <summary>
/// One workspace the caller belongs to, as <c>GET /api/me/tenants</c> publishes it.
/// </summary>
/// <param name="Id">The tenant's identity.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Slug">Its canonical slug.</param>
/// <param name="Status">Its operational state, as a stable name.</param>
/// <remarks>
/// The same four fields as <c>TenantResponse</c>, and deliberately a separate
/// type rather than a reuse. This is a membership list: it must never grow a
/// member count, a list of other members, a role or an owner, and keeping it
/// distinct means a change to the single-tenant contract cannot add one here by
/// accident.
/// </remarks>
public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, string Status);
