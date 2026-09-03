using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests;

/// <summary>
/// Executable guard for the Infrastructure layer: it is an outer layer and must
/// never depend on the composition root.
/// </summary>
/// <remarks>
/// Only forbidden dependencies are asserted. An assembly reference is emitted
/// solely when a type is actually used, so the absence of one proves nothing
/// about intent -- but its presence proves a violation.
/// </remarks>
public sealed class InfrastructureDependencyTests
{
    [Fact]
    public void Infrastructure_does_not_reference_api()
    {
        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Infrastructure.dll")
                .Where(name => name.Equals("OmniAI.Api", StringComparison.Ordinal))
        ];

        Assert.Empty(offenders);
    }
}
