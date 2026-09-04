namespace OmniAI.Application.Ai;

/// <summary>
/// Raised when text offered as a conversational turn cannot be accepted.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An Application failure, deliberately not a Domain one.</strong> The
/// rules it reports -- a message is required, and is bounded in length -- are
/// not business rules about what an AI agent is. They are limits this platform
/// places on a request it will process, in the same family as a maximum upload
/// size. Raising <c>DomainValidationException</c> here would put a transport and
/// resource concern in the model and would claim an aggregate rejected something
/// no aggregate was asked about.
/// </para>
/// <para>
/// Distinct from the two AI runtime failures for the reason that matters to a
/// caller: this one says the caller sent something unusable and can fix it,
/// whereas <see cref="AiRuntimeUnavailableException"/> and
/// <see cref="AiResponseInvalidException"/> say the request was fine and the
/// platform's own AI runtime could not answer it. Three flat types, no base
/// class and no error-code catalogue: each names one outcome that leads to a
/// different action.
/// </para>
/// <para>
/// Transport-neutral by construction -- no status code, no
/// <c>ProblemDetails</c>, no HTTP vocabulary. Its message describes the rule and
/// the size of the offending value, never the value itself: a preview message is
/// text somebody typed, and echoing it into an error body would put it somewhere
/// it was never meant to go.
/// </para>
/// </remarks>
public sealed class AiTurnValidationException : Exception
{
    public AiTurnValidationException()
        : base("The message could not be accepted.")
    {
    }

    public AiTurnValidationException(string message)
        : base(message)
    {
    }

    public AiTurnValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
