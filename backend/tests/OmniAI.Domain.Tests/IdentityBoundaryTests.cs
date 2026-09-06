using OmniAI.Testing;

namespace OmniAI.Domain.Tests;

/// <summary>
/// Executable guard: credentials and HTTP authentication never reach the Domain.
/// </summary>
/// <remarks>
/// <see cref="DomainDependencyTests"/> already forbids every non-BCL reference,
/// which covers these by implication. This names them anyway, because a guard
/// that fails with "OmniAI.Domain references Microsoft.AspNetCore.Identity" says
/// exactly what went wrong and why it matters, where a generic "unexpected
/// reference" leaves the next reader to work out whether it was a mistake or a
/// decision.
/// <para>
/// The rule it protects: the Domain models a business, and a password hash is not
/// part of one. <c>TenantMembership</c> holds a <c>UserId</c>, which is a value;
/// the account behind it, its credentials and how somebody proved they hold them
/// all belong to the identity provider in Infrastructure.
/// </para>
/// </remarks>
public sealed class IdentityBoundaryTests
{
    private const string DomainAssemblyFile = "OmniAI.Domain.dll";

    [Fact]
    public void Domain_does_not_reference_asp_net_core_identity()
    {
        string[] forbidden =
        [
            "Microsoft.AspNetCore.Identity",
            "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
            "Microsoft.Extensions.Identity.Core",
            "Microsoft.Extensions.Identity.Stores",
        ];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames(DomainAssemblyFile)
                .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Domain_does_not_reference_http_or_cookie_authentication()
    {
        // HttpContext, ClaimsPrincipal handling, cookie authentication handlers
        // and the authorization pipeline all live under Microsoft.AspNetCore.
        // Any of them here would mean the Domain had started to care how a caller
        // was identified.
        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames(DomainAssemblyFile)
                .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Domain_names_no_authentication_type_in_its_public_surface()
    {
        // The reference check above cannot see a type named only in a signature
        // that was never compiled against -- and more usefully, this states the
        // rule in terms a reader recognises. System.Security.Claims is part of the
        // base class library, so it would pass the reference guards; it must still
        // never appear here.
        string[] offenders =
        [
            .. typeof(AssemblyMarker).Assembly.GetTypes()
                .SelectMany(type => type.GetProperties())
                .Select(property => property.PropertyType.FullName ?? string.Empty)
                .Where(name =>
                    name.Contains("ClaimsPrincipal", StringComparison.Ordinal)
                    || name.Contains("ClaimsIdentity", StringComparison.Ordinal)
                    || name.Contains("HttpContext", StringComparison.Ordinal)
                    || name.Contains("IdentityUser", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }
}
