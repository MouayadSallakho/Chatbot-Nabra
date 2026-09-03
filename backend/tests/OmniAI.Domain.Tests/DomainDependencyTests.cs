using OmniAI.Testing;

namespace OmniAI.Domain.Tests;

/// <summary>
/// Executable guard for the innermost Clean Architecture rule: the Domain layer
/// depends on nothing but the base class library. A failure here means a
/// dependency was introduced in the wrong direction, or that an infrastructure
/// concern leaked inward.
/// </summary>
public sealed class DomainDependencyTests
{
    private const string DomainAssemblyFile = "OmniAI.Domain.dll";

    [Fact]
    public void Domain_references_no_other_solution_assembly()
    {
        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames(DomainAssemblyFile)
                .Where(name => name.StartsWith("OmniAI.", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Domain_references_no_third_party_or_infrastructure_package()
    {
        string[] allowed = ["System", "netstandard", "mscorlib"];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames(DomainAssemblyFile)
                .Where(name => !allowed.Any(prefix =>
                    name.Equals(prefix, StringComparison.Ordinal) ||
                    name.StartsWith(prefix + ".", StringComparison.Ordinal)))
        ];

        Assert.Empty(offenders);
    }
}
