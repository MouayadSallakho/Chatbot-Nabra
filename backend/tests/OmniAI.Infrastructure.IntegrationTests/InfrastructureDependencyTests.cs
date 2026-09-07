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

    [Fact]
    public void The_redis_client_stops_at_this_layer()
    {
        // Phase 16's counterpart to the OpenAI guard. The Redis client is expected
        // here and nowhere else: the Application owns IPublicChatProtection and
        // asks whether a public request may proceed, and this is the only assembly
        // allowed to know the answer is a Lua script against a counter store.
        //
        // Asserted as a positive here -- Infrastructure really does reference it --
        // so that the negative guards in the other layers cannot pass merely
        // because the package was dropped from the solution altogether.
        Assert.Contains(
            "StackExchange.Redis",
            AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Infrastructure.dll"),
            StringComparer.Ordinal);
    }
}
