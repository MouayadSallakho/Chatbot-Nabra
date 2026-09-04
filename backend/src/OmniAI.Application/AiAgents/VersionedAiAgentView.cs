using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents;

/// <summary>
/// What a use case hands back: the agent's read model, and the version it is at.
/// </summary>
/// <param name="View">The agent information.</param>
/// <param name="Version">The version marker for this revision.</param>
/// <remarks>
/// The version is deliberately <em>beside</em> the view rather than inside it.
/// <see cref="AiAgentView"/> is what becomes the JSON body, and the accepted
/// contract is seven fields with no persistence metadata; folding a version into
/// it would put that metadata one serialisation away from the wire. Keeping it
/// out here means the API can only publish it where it belongs -- in a header.
/// </remarks>
public sealed record VersionedAiAgentView(AiAgentView View, ConcurrencyToken Version);
