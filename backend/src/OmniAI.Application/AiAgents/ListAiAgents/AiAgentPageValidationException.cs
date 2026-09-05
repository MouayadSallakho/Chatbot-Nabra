namespace OmniAI.Application.AiAgents.ListAiAgents;

/// <summary>
/// Raised when a listing request asks for a page this platform will not serve.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An Application failure, deliberately not a Domain one.</strong> How
/// many agents a caller may read in one request is not a rule about what an AI
/// agent is; no aggregate was asked and none refused. It is a limit this
/// platform places on a request it is willing to process, in the same family as
/// a maximum upload size -- which is why it follows
/// <c>AiTurnValidationException</c> rather than raising
/// <c>DomainValidationException</c> and claiming the model rejected something.
/// </para>
/// <para>
/// <strong>Refusal rather than clamping.</strong> An out-of-range page size
/// could be quietly reduced to the maximum, and that is the more common choice.
/// It is the wrong one here: a caller that asked for five hundred and received a
/// hundred has no way to tell a truncated page from the end of the list, so it
/// stops paginating and silently loses data. Being told is recoverable; guessing
/// is not.
/// </para>
/// <para>
/// Transport-neutral by construction: no status code, no <c>ProblemDetails</c>,
/// no HTTP vocabulary. The message names the rule and the bound, so a caller can
/// correct the request from the response alone.
/// </para>
/// </remarks>
public sealed class AiAgentPageValidationException : Exception
{
    public AiAgentPageValidationException()
        : base("The requested page could not be served.")
    {
    }

    public AiAgentPageValidationException(string message)
        : base(message)
    {
    }

    public AiAgentPageValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The page number was below the first page.</summary>
    internal static AiAgentPageValidationException ForPage(int page) =>
        new($"Page must be {AiAgentPage.DefaultPage} or greater, but was {page}.");

    /// <summary>The page size was outside the allowed range.</summary>
    internal static AiAgentPageValidationException ForPageSize(int pageSize) =>
        new($"Page size must be between 1 and {AiAgentPage.MaxPageSize}, but was {pageSize}.");
}
