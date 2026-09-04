using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents;

/// <summary>
/// The precondition check every conditional AI agent mutation performs before it
/// touches the aggregate.
/// </summary>
/// <remarks>
/// <para>
/// One method, shared by four handlers that would otherwise each carry the same
/// four lines. It is not a framework and not a pipeline: there is no interface,
/// no generic parameter, no registration and no reflection — a handler calls it
/// the way it would call any other helper, and can stop calling it without
/// anything else changing.
/// </para>
/// <para>
/// <strong>Why this exists at all, when the database also checks.</strong> The
/// storage layer refuses a write whose expected version no longer matches, which
/// covers every mutation that actually writes something. It cannot cover a
/// mutation that writes <em>nothing</em>: activating an already active agent
/// changes no property, so no statement is issued and there is no predicate to
/// fail. Without this check, a caller holding a hopelessly stale version could
/// send that request and receive a cheerful success — a precondition silently
/// skipped because the operation happened to be a no-op. HTTP preconditions are
/// evaluated against the resource, not against whether the request would have
/// changed it.
/// </para>
/// <para>
/// <strong>Why it runs before the Domain operation.</strong> A caller whose
/// version is stale has not seen the agent as it is now, so it is told that first
/// — even when the operation it asked for would also have been refused on its
/// merits. Disabling a draft with an out-of-date version reports the stale
/// version, not the draft rule: the draft rule describes an agent the caller has
/// never seen, and reporting it would answer a question they did not ask while
/// leaking the current state of a resource whose version they failed to present.
/// </para>
/// </remarks>
internal static class AiAgentConcurrency
{
    /// <summary>
    /// Throws unless the stored agent is still at the version the caller expected.
    /// </summary>
    /// <exception cref="AiAgentVersionMismatchException">The versions differ.</exception>
    public static void RequireExpectedVersion(VersionedAiAgent stored, ConcurrencyToken expected)
    {
        ArgumentNullException.ThrowIfNull(stored);

        // Ordinal equality on an opaque value. Nothing here parses the token,
        // compares it for ordering, or knows what it is made of.
        if (stored.Version != expected)
        {
            throw AiAgentVersionMismatchException.ForAgent(stored.Agent.Id.Value);
        }
    }
}
