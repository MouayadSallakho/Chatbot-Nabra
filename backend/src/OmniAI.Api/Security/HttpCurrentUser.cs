using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OmniAI.Application.Security;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Security;

/// <summary>
/// Reads the authenticated caller out of the current HTTP request.
/// </summary>
/// <remarks>
/// The one place in the platform where a <c>ClaimsPrincipal</c> is turned into a
/// <see cref="UserId"/>. Everything above it -- every handler, every use case --
/// sees only <see cref="ICurrentUser"/> and never learns that authentication
/// arrived in a cookie, or arrived over HTTP at all.
/// <para>
/// <strong>The claim type is read from Identity's own options, not hard-coded.</strong>
/// <c>IdentityOptions.ClaimsIdentity.UserIdClaimType</c> is what
/// <c>UserClaimsPrincipalFactory</c> writes the id into. Spelling
/// <c>ClaimTypes.NameIdentifier</c> here would work today and would silently stop
/// working the moment somebody configured a different claim type -- and "silently"
/// is the problem: the failure would be every signed-in person appearing
/// anonymous, which reads like a session bug rather than a configuration one.
/// </para>
/// <para>
/// <strong>A malformed or missing id means not authenticated.</strong> Not an
/// exception, and not a default <see cref="UserId"/>. A principal that says it is
/// authenticated but carries no usable id cannot be matched against a membership
/// row, so the honest answer is that nobody is signed in -- and the request is
/// then refused with <c>401</c>, which is exactly what somebody holding a broken
/// cookie needs to be told.
/// </para>
/// </remarks>
internal sealed class HttpCurrentUser(
    IHttpContextAccessor accessor,
    IOptions<IdentityOptions> identityOptions) : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor =
        accessor ?? throw new ArgumentNullException(nameof(accessor));

    private readonly IOptions<IdentityOptions> _identityOptions =
        identityOptions ?? throw new ArgumentNullException(nameof(identityOptions));

    public bool IsAuthenticated => Id is not null;

    public UserId? Id
    {
        get
        {
            ClaimsPrincipal? principal = _accessor.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            string claimType = _identityOptions.Value.ClaimsIdentity.UserIdClaimType;
            string? value = principal.FindFirstValue(claimType);

            if (!Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid parsed)
                || parsed == Guid.Empty)
            {
                return null;
            }

            return UserId.From(parsed);
        }
    }
}
