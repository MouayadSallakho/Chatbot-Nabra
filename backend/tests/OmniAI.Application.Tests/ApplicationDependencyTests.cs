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
}
