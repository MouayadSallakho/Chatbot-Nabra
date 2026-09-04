namespace OmniAI.Application.AiAgents;

/// <summary>
/// Raised when a conditional write names a version of an agent that is no longer
/// the current one.
/// </summary>
/// <remarks>
/// <para>
/// Covers both ways that can happen, because to the caller they are the same
/// answer -- "what you were looking at is out of date":
/// </para>
/// <list type="number">
/// <item>
/// <strong>Client think time.</strong> The version was already superseded before
/// the request arrived. Somebody read the agent, went away, and by the time they
/// pressed save a colleague had changed it. This is the common case and the one
/// this type exists for.
/// </item>
/// <item>
/// <strong>A race inside the request.</strong> The version was current when the
/// request began and another transaction committed before it finished writing.
/// Rare, but the same loss if it were allowed through.
/// </item>
/// </list>
/// <para>
/// Distinct from <c>DomainStateException</c>, and the distinction matters. A
/// Domain state conflict means the operation contradicts the agent as it stands
/// -- disabling a draft -- and would still be refused if the caller re-read
/// first. This means the operation might be perfectly fine; the caller simply
/// has not seen what the agent looks like now.
/// </para>
/// <para>
/// Transport-neutral and provider-neutral by construction: no status code, no
/// <c>ProblemDetails</c>, no <c>ETag</c>, no <c>DbUpdateConcurrencyException</c>,
/// EF Core type, Npgsql type, SQLSTATE or SQL text. Infrastructure translates the
/// database's answer into this before it crosses the boundary.
/// </para>
/// <para>
/// <strong>Nothing retries automatically.</strong> Reapplying the caller's intent
/// to state they have never seen is exactly the lost update this whole mechanism
/// exists to prevent. The caller is told, and decides.
/// </para>
/// </remarks>
public sealed class AiAgentVersionMismatchException : Exception
{
    public AiAgentVersionMismatchException()
        : base("The AI agent has changed since it was read. No change was applied.")
    {
    }

    public AiAgentVersionMismatchException(string message)
        : base(message)
    {
    }

    public AiAgentVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The agent that could not be updated, when the thrower knew it.</summary>
    public Guid? AiAgentId { get; private init; }

    /// <summary>Creates the exception for a known agent.</summary>
    public static AiAgentVersionMismatchException ForAgent(
        Guid aiAgentId,
        Exception? innerException = null)
    {
        string message =
            $"The AI agent '{aiAgentId}' has changed since the version you supplied was read. " +
            "No change was applied. Read the agent again and reapply the change if it is still wanted.";

        return innerException is null
            ? new AiAgentVersionMismatchException(message) { AiAgentId = aiAgentId }
            : new AiAgentVersionMismatchException(message, innerException) { AiAgentId = aiAgentId };
    }
}
