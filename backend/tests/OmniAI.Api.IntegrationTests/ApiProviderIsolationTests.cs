using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests;

/// <summary>
/// Executable guard: the vendor stops at Infrastructure.
/// </summary>
/// <remarks>
/// Phase 9 added a provider SDK to exactly one production project. This asserts
/// the API is not that project — it maps HTTP to use cases and has no business
/// naming a vendor's types.
/// <para>
/// The check is against the compiled assembly's metadata, so it is direct
/// evidence rather than an inference from source text. Note that the <em>test</em>
/// project deliberately does reference the SDK, to build a canned transport; this
/// looks at the production assembly instead.
/// </para>
/// </remarks>
public sealed class ApiProviderIsolationTests
{
    [Fact]
    public void The_api_assembly_references_no_ai_provider_sdk()
    {
        string[] forbidden =
        [
            "OpenAI",
            "Azure.AI.OpenAI",
            "Anthropic",
            "Anthropic.SDK",
            "Betalgo.OpenAI",
            "OpenAI_API",
            "Microsoft.Extensions.AI",
            "Microsoft.SemanticKernel",
            "LangChain",
        ];

        string[] offenders =
        [
            .. AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Api.dll")
                .Where(name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_api_assembly_does_not_reference_the_provider_transport_stack()
    {
        // System.ClientModel is the SDK's pipeline. Its appearance here would mean
        // the API had started configuring or handling provider transport, which is
        // Infrastructure's job.
        Assert.DoesNotContain(
            AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Api.dll"),
            name => name.Equals("System.ClientModel", StringComparison.Ordinal));
    }
}
