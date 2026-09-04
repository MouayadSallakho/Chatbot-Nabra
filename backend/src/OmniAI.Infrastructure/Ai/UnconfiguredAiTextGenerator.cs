using OmniAI.Application.Ai;

namespace OmniAI.Infrastructure.Ai;

/// <summary>
/// The AI text generator a deployment gets when no provider has been configured.
/// </summary>
/// <remarks>
/// <para>
/// It refuses every request, immediately and explicitly. That is the entire
/// implementation, and it is what lets the application start, serve health checks
/// and run the whole agent management slice on a deployment where AI is not set
/// up yet -- while making the one operation that genuinely needs a provider say so
/// plainly instead of failing in some incidental way.
/// </para>
/// <para>
/// <strong>It never fabricates an answer.</strong> Not a canned reply, not an echo
/// of the caller's own message, not a template, not a "sorry, I cannot help"
/// sentence that would look exactly like a real agent's reply. Anything that
/// returned successfully here would put text of unknown origin in front of somebody
/// evaluating whether their agent works -- and the failure mode is that they
/// conclude it does. A configuration gap must look like a configuration gap.
/// </para>
/// <para>
/// <strong>It never falls back.</strong> There is no second provider to try and no
/// silent substitution. Falling back invisibly is how a deployment ends up sending
/// its data somewhere its operator did not choose.
/// </para>
/// <para>
/// <strong>This is not a test double.</strong> It is the honest production
/// behaviour for a deployment with no AI provider, and it is registered as the
/// real implementation. The deterministic fake that returns configured text lives
/// in the test projects and is never registered by the composition root -- which
/// is the difference between "the application admits it cannot do this" and "the
/// application quietly pretends".
/// </para>
/// <para>
/// It reads no configuration, holds no state and opens no connection. There is no
/// API key lookup here, no environment variable, no provider settings section and
/// no HTTP client: the phase that adds a real provider adds all of that, and until
/// then there is nothing for this type to read.
/// </para>
/// </remarks>
internal sealed class UnconfiguredAiTextGenerator : IAiTextGenerator
{
    /// <summary>Always throws.</summary>
    /// <remarks>
    /// Throws directly rather than returning a faulted task. Every caller awaits
    /// the call immediately, so the two are indistinguishable to them, and a
    /// direct throw keeps the method's one behaviour on one line.
    /// <para>
    /// The request is not inspected, not logged and not retained. It carries the
    /// agent's instructions, and there is no reason for a component that will do
    /// nothing with them to touch them at all.
    /// </para>
    /// </remarks>
    /// <exception cref="AiRuntimeUnavailableException">Always.</exception>
    public Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new AiRuntimeUnavailableException();
}
