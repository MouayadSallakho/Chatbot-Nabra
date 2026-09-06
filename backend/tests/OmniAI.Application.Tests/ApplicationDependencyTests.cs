using OmniAI.Testing;

namespace OmniAI.Application.Tests;

/// <summary>
/// Executable guard for the Application layer: it may depend inward on Domain,
/// and must never depend outward on Infrastructure or Api.
/// </summary>
/// <remarks>
/// Only forbidden dependencies are asserted. An assembly reference is emitted
/// solely when a type is actually used, so the absence of one proves nothing
/// about intent -- but its presence proves a violation.
/// </remarks>
public sealed class ApplicationDependencyTests
{
    [Fact]
    public void Application_references_neither_infrastructure_nor_api()
    {
        string[] forbidden = ["OmniAI.Infrastructure", "OmniAI.Api"];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Application.dll")
                .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_references_no_persistence_technology()
    {
        // Phase 5 gave the Application layer its first persistence contract, and
        // the whole value of that contract is that it names no technology. A
        // reference to EF Core or Npgsql appearing here would mean an IQueryable,
        // a DbSet, a DbContext or a provider exception had reached a use case --
        // which is the leak this guard exists to make impossible to merge
        // unnoticed, rather than to catch in review.
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Npgsql",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
        ];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Application.dll")
                .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_references_no_identity_or_credential_technology()
    {
        // Phase 12 gave the Application layer an opinion about who is calling, and
        // the whole point of ICurrentUser is that it holds that opinion without
        // knowing how the answer arrived. A reference to Identity here would mean
        // a use case had started dealing in UserManager, IdentityUser or a
        // password hash -- all of which belong to Infrastructure, behind a
        // contract this layer owns.
        string[] forbidden =
        [
            "Microsoft.AspNetCore.Identity",
            "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
            "Microsoft.Extensions.Identity.Core",
            "Microsoft.Extensions.Identity.Stores",
        ];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Application.dll")
                .Where(name => forbidden.Contains(name, StringComparer.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_names_no_http_authentication_type()
    {
        // System.Security.Claims is in the base class library, so the reference
        // guards would not catch a ClaimsPrincipal appearing in a use case
        // signature. This does. ICurrentUser exists precisely so that no handler
        // ever has to name one.
        string[] offenders =
        [
            .. typeof(Application.AssemblyMarker).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods())
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType.FullName ?? string.Empty)
                .Where(name =>
                    name.Contains("ClaimsPrincipal", StringComparison.Ordinal)
                    || name.Contains("ClaimsIdentity", StringComparison.Ordinal)
                    || name.Contains("HttpContext", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_references_no_web_or_transport_framework()
    {
        // The Application layer decides outcomes, not status codes. An ASP.NET
        // Core reference would mean a use case had started to care how its result
        // is transported -- and Phase 5 deliberately put every status code, every
        // ProblemDetails and the whole HTTP contract in the API project instead.
        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Application.dll")
                .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }
}
