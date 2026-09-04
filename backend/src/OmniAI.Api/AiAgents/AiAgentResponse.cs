using OmniAI.Application.AiAgents;
using OmniAI.Domain.AiAgents;

namespace OmniAI.Api.AiAgents;

/// <summary>
/// The AI agent representation returned by the AI agent endpoints.
/// </summary>
/// <param name="Id">The agent's identity.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="Name">The canonical display name.</param>
/// <param name="Instructions">The canonical instruction text, formatting preserved.</param>
/// <param name="Status">The lifecycle state, as a stable name such as <c>Draft</c>.</param>
/// <param name="CreatedAt">When the agent was created, in UTC.</param>
/// <param name="UpdatedAt">When the agent last actually changed, in UTC.</param>
/// <remarks>
/// The published HTTP contract, and the only thing a client sees. It is a
/// separate type from <c>AiAgentView</c> for one concrete reason: <c>Status</c>
/// has to appear on the wire as a name, and choosing that representation is a
/// transport decision. Keeping it here means the Application layer never has to
/// hold an opinion about JSON, and the wire format cannot drift because someone
/// changed a read model.
/// <para>
/// Seven fields, all of them owned by the Domain today. There is no model name,
/// no provider id, no temperature, no token limit, no external assistant or
/// thread id, no knowledge source, no tool and no channel: the aggregate has
/// none of those, and publishing a field with nothing behind it would be a
/// promise this API cannot keep. There is also no <c>xmin</c>, no row version and
/// no other persistence metadata -- how the database detects a conflicting write
/// is not part of the contract.
/// </para>
/// </remarks>
public sealed record AiAgentResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Instructions,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps an Application read model onto the HTTP contract.</summary>
    internal static AiAgentResponse From(AiAgentView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new AiAgentResponse(
            view.Id,
            view.TenantId,
            view.Name,
            view.Instructions,
            Serialise(view.Status),
            view.CreatedAt,
            view.UpdatedAt);
    }

    /// <summary>
    /// The wire name for a lifecycle state.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to <c>ToString()</c> or a JSON enum
    /// converter. Both of those would make the published contract a side effect
    /// of an enum member's spelling or of a serialiser setting, so renaming a
    /// member or changing a global option would silently change what clients
    /// receive. Here the mapping is the contract, and adding a status without
    /// deciding its public name fails loudly instead of leaking an integer
    /// ordinal or an unexpected string.
    /// </remarks>
    private static string Serialise(AiAgentStatus status) => status switch
    {
        AiAgentStatus.Draft => "Draft",
        AiAgentStatus.Active => "Active",
        AiAgentStatus.Disabled => "Disabled",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "This AI agent status has no published API representation."),
    };
}
