using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;

namespace OmniAI.Application.AiAgents.Persistence;

/// <summary>
/// An agent as it was read from storage, together with the version it was read
/// at.
/// </summary>
/// <param name="Agent">The aggregate.</param>
/// <param name="Version">The version marker for the revision that was read.</param>
/// <remarks>
/// The two travel together because they are only meaningful together: a caller
/// that has the aggregate but not the version cannot make a conditional write,
/// and a version without the aggregate it came from says nothing.
/// <para>
/// Kept separate from the aggregate rather than added to it, so that
/// <c>AiAgent</c> stays a pure business object with no persistence metadata --
/// see the accepted Domain decision.
/// </para>
/// </remarks>
public sealed record VersionedAiAgent(AiAgent Agent, ConcurrencyToken Version);
