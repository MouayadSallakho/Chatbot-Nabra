namespace OmniAI.Application.Ai;

/// <summary>
/// Raised when an AI text generator returns something that is not a usable
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An upstream failure, not a validation failure.</strong> The caller's
/// request was fine and the runtime was available; what came back was unusable.
/// That distinction is the reason this is a separate type from
/// <see cref="AiTurnValidationException"/>: nothing the caller changes about its
/// request would help, so telling them their input was wrong would send them to
/// fix the one thing that was not broken.
/// </para>
/// <para>
/// It is also separate from <see cref="AiRuntimeUnavailableException"/>, which
/// says the platform never asked anyone. Here something answered and the answer
/// did not hold. A caller that can distinguish the two can tell "AI is switched
/// off" from "AI is misbehaving", and so can an operator reading logs.
/// </para>
/// <para>
/// <strong>Empty is a failure, never a result.</strong> An empty or whitespace-only
/// reply must not reach a person as though the agent had chosen to say nothing.
/// It is not an answer, and presenting it as one would make a broken integration
/// look like an unhelpful agent -- the most expensive kind of bug to notice,
/// because nothing appears to be wrong.
/// </para>
/// <para>
/// Transport-neutral and provider-neutral by construction: no status code, no
/// <c>ProblemDetails</c>, no vendor exception type, no HTTP response body, no
/// provider request id. Its message describes the shape of the breach and nothing
/// about the provider that caused it, because that text can reach a client.
/// </para>
/// <para>
/// Deliberately not the root of an error hierarchy. There is no rate-limit type,
/// no quota type, no moderation type and no provider-specific code, because
/// nothing has yet observed how a real provider reports those. Inventing the
/// taxonomy first would mean mapping real failures onto guessed categories.
/// </para>
/// </remarks>
public sealed class AiResponseInvalidException : Exception
{
    /// <summary>The single message every occurrence carries.</summary>
    /// <remarks>
    /// One fixed sentence, so no detail of the failing provider can be threaded
    /// into text that is shown to a caller.
    /// </remarks>
    private const string InvalidResponse =
        "The AI runtime returned no usable response. No answer was produced.";

    public AiResponseInvalidException()
        : base(InvalidResponse)
    {
    }

    public AiResponseInvalidException(string message)
        : base(message)
    {
    }

    public AiResponseInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
