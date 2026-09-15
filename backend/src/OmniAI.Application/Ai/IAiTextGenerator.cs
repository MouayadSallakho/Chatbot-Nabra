using OmniAI.Application.Ai.Usage;

namespace OmniAI.Application.Ai;

/// <summary>
/// Generates text from an agent's instructions and a conversation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One capability, one method.</strong> This is deliberately not an
/// <c>IAiProvider</c> carrying embeddings, image generation, speech,
/// transcription, moderation, tool calling, structured output, streaming and
/// model management. A broad interface like that would have to be implemented in
/// full by every adapter -- most of it as methods that throw -- and the platform
/// would end up asking "does this provider really support that?" at runtime,
/// which is the question a type system exists to answer. When the platform needs
/// embeddings it gets a separate contract, and an adapter implements the ones it
/// can actually do.
/// </para>
/// <para>
/// <strong>Owned by the Application, implemented by Infrastructure.</strong> The
/// abstraction lives here because the use cases are what need text generated;
/// vendors live outside because a vendor is a detail. Nothing in this file names
/// OpenAI, Anthropic, Meta, Azure or any SDK, and nothing in it references EF
/// Core, Npgsql or ASP.NET -- an architecture test asserts that rather than
/// leaving it to review.
/// </para>
/// <para>
/// <strong>The Domain never sees this.</strong> <c>AiAgent</c> holds a name,
/// instructions and a lifecycle. It does not generate anything, has no reference
/// to this interface, and gains no method that would need one.
/// </para>
/// <para>
/// <strong>Implementations must resolve authorisation before they are
/// called.</strong> This contract carries an agent's instructions, which are
/// business configuration and may describe internal rules. A caller is
/// responsible for establishing that the requester is entitled to that agent
/// <em>before</em> building a request from it -- for agent preview, that means the
/// tenant-scoped lookup runs first, so a cross-tenant request is refused without
/// any instruction text ever being assembled, let alone sent anywhere.
/// </para>
/// </remarks>
public interface IAiTextGenerator
{
    /// <summary>
    /// Produces a reply for the supplied instructions and conversation.
    /// </summary>
    /// <param name="request">The instructions and turns to answer.</param>
    /// <param name="cancellationToken">
    /// Cancellation for the whole operation, including any network call an
    /// implementation makes.
    /// </param>
    /// <returns>The generated reply. Never null.</returns>
    /// <remarks>
    /// <para>
    /// Asynchronous and cancellable because every real implementation of this will
    /// be a network call to a service that can be slow. A synchronous signature
    /// would block a request thread for the duration and would give a caller that
    /// has already given up -- a closed browser tab, a client timeout -- no way to
    /// stop work nobody is waiting for. The token is passed through rather than
    /// observed only at the start, so cancellation reaches the call in flight.
    /// </para>
    /// <para>
    /// Nothing retries. A later phase will decide what to retry once a real
    /// provider has shown which of its failures are transient; retrying now, on
    /// guesses, would multiply load during exactly the incidents where that is
    /// most harmful.
    /// </para>
    /// </remarks>
    /// <exception cref="AiRuntimeUnavailableException">
    /// No AI runtime is configured on this deployment.
    /// </exception>
    /// <exception cref="AiResponseInvalidException">
    /// The runtime answered with something unusable.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <param name="usage">
    /// Whose money this spends and what for. Required, and a parameter rather
    /// than an ambient value: a new call site cannot compile without saying which
    /// workspace to attribute the call to, which is the only way attribution
    /// stays correct once background work exists.
    /// </param>
    Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        AiUsageContext usage,
        CancellationToken cancellationToken = default);
}
