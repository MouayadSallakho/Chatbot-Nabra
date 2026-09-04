namespace OmniAI.Application.Ai;

/// <summary>
/// Raised when the platform has no AI text generation runtime it can use.
/// </summary>
/// <remarks>
/// <para>
/// The honest answer to "generate something" when no provider is configured. It
/// exists so that state has a name: without it the only options are a fabricated
/// answer, which is worse than any error, or an unexplained fault, which tells an
/// operator nothing about what to fix.
/// </para>
/// <para>
/// <strong>It is a temporary condition, and says so.</strong> The request was
/// well-formed and would be answerable once a provider is configured, which is
/// why it is distinct from every failure that means "the caller must change
/// something". Nothing here retries: retrying an unconfigured runtime cannot
/// succeed, and a retry loop would turn a clear configuration message into a slow
/// one.
/// </para>
/// <para>
/// <strong>The message names no provider and no setting.</strong> It reaches a
/// client, so it says what is true at that level -- the capability is not
/// available -- without disclosing which vendor the platform intends to use, what
/// the configuration key is called, or whether a key is present but rejected. An
/// operator learns which by reading the deployment's configuration, not by
/// probing an endpoint.
/// </para>
/// <para>
/// Transport-neutral by construction: no status code, no <c>ProblemDetails</c>,
/// no HTTP vocabulary. The API decides how an unavailable capability is
/// reported.
/// </para>
/// </remarks>
public sealed class AiRuntimeUnavailableException : Exception
{
    /// <summary>The single message every occurrence carries.</summary>
    private const string Unavailable =
        "AI text generation is not available on this deployment. No answer was produced.";

    public AiRuntimeUnavailableException()
        : base(Unavailable)
    {
    }

    public AiRuntimeUnavailableException(string message)
        : base(message)
    {
    }

    public AiRuntimeUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
