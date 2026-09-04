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
