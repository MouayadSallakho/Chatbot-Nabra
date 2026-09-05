using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Domain.AiAgents;

namespace OmniAI.Api.AiAgents;

/// <summary>
/// One AI agent as the listing endpoint publishes it.
/// </summary>
/// <param name="Id">The agent's identity.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="Name">The canonical display name.</param>
/// <param name="Status">The lifecycle state, as a stable name such as <c>Draft</c>.</param>
/// <param name="CreatedAt">When the agent was created, in UTC.</param>
/// <param name="UpdatedAt">When the agent last actually changed, in UTC.</param>
/// <remarks>
/// <para>
/// Six fields, and the absent one is deliberate: <strong>there are no
/// instructions here.</strong> They can run to twenty thousand characters, a
/// list shows none of them, and the query behind this response does not select
/// the column -- so publishing them would mean adding work as well as leaking
/// the tenant's configured business behaviour into a listing.
/// </para>
/// <para>
/// No entity tag either, in the body or the header. A summary is not a
/// representation a caller may write against: a conditional write needs the
/// version of the thing actually read, which is what the single-agent endpoint
/// returns. Publishing a tag here would invite a client to mutate against a
/// version it only half read.
/// </para>
/// <para>
/// And no <c>xmin</c>, row version, provider, model or other persistence and
/// vendor metadata -- the same rule the single-agent response follows.
/// </para>
/// </remarks>
public sealed record AiAgentSummaryResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps an Application summary onto the HTTP contract.</summary>
    internal static AiAgentSummaryResponse From(AiAgentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new AiAgentSummaryResponse(
            summary.Id,
            summary.TenantId,
            summary.Name,
            Serialise(summary.Status),
            summary.CreatedAt,
            summary.UpdatedAt);
    }

    /// <summary>
    /// The wire name for a lifecycle state.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to <c>ToString()</c> or a JSON enum
    /// converter, and matching <see cref="AiAgentResponse"/> exactly. A list and
    /// a detail view disagreeing about how a status is spelled would be a
    /// contract bug that only showed up in a client, so both spell it here
    /// explicitly and adding a status without deciding its public name fails
    /// loudly in both places.
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

/// <summary>
/// One page of a tenant's AI agents.
/// </summary>
/// <param name="Items">The agents on this page, most recently changed first.</param>
/// <param name="Page">The 1-based page number that was served.</param>
/// <param name="PageSize">The maximum number of items a page may hold.</param>
/// <param name="TotalCount">How many agents this tenant has in total.</param>
/// <remarks>
/// The envelope is part of the contract rather than a bare array, because a bare
/// array has nowhere to say which page it is or how many there are -- and a
/// client that cannot tell "twenty of twenty" from "twenty of two hundred"
/// cannot build a correct pager. Putting that in headers instead would hide it
/// from anybody reading the response body.
/// <para>
/// There is no <c>totalPages</c> and no <c>hasNext</c>: both are arithmetic the
/// client can do, and publishing derived values means publishing two things that
/// can disagree.
/// </para>
/// </remarks>
public sealed record AiAgentPageResponse(
    IReadOnlyList<AiAgentSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Maps an Application page onto the HTTP contract.</summary>
    internal static AiAgentPageResponse From(AiAgentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new AiAgentPageResponse(
            [.. page.Items.Select(AiAgentSummaryResponse.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}
