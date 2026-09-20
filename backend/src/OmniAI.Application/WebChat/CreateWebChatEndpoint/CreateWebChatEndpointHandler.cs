using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
namespace OmniAI.Application.WebChat.CreateWebChatEndpoint;

/// <summary>Publish a chat link for one of the workspace's agents.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="AiAgentId">The agent that will answer.</param>
public sealed record CreateWebChatEndpointCommand(Guid TenantId, Guid AiAgentId);

/// <summary>
/// Creates a website chat endpoint, as a draft.
/// </summary>
/// <remarks>
/// <strong>It is created switched off.</strong> Creating a chat link and opening
/// it to the internet are two decisions, and merging them would mean a mistyped
/// agent selection is live the instant it is saved. Activation is its own
/// operation with its own permission check and its own precondition.
/// </remarks>
public sealed class CreateWebChatEndpointHandler(
    IWorkspaceEntitlementService entitlements,
    IWebChatEndpointRepository endpoints,
    IAiAgentRepository agents,
    IWebChatPublicKeys publicKeys,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly IWebChatPublicKeys _publicKeys =
        publicKeys ?? throw new ArgumentNullException(nameof(publicKeys));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The new endpoint and its version, or <see langword="null"/> when the caller
    /// is not entitled to manage this workspace's channels.
    /// </returns>
    /// <exception cref="WebChatAgentUnusableException">
    /// The agent does not belong to this workspace, or already has a chat link.
    /// </exception>
    public async Task<Versioned<WebChatEndpointView>?> HandleAsync(
        CreateWebChatEndpointCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WebsiteChatManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        // Read through the tenant-scoped lookup, so naming another workspace's
        // agent cannot publish a chat link that answers from it. This is the only
        // check that stands between a guessed agent id and somebody else's agent
        // talking to the public on this workspace's URL.
        Versioned<AiAgent>? agent = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        if (agent is null)
        {
            throw WebChatAgentUnusableException.NotFound();
        }

        if (await _endpoints
            .ExistsForAgentAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false))
        {
            throw WebChatAgentUnusableException.AlreadyPublished();
        }

        // A draft endpoint may be created for an agent in any state. Publishing
        // is what requires an active agent, and it is checked again on every
        // public request rather than trusted from here -- an agent's status can
        // change any time after this line.
        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            tenantId,
            agentId,
            _publicKeys.Create(),
            _clock.GetUtcNow());

        // Permission said this person may create one; the plan says whether the
        // workspace may hold another. Two questions, asked in that order, and
        // neither answers the other. The count and the insert happen together
        // under a workspace-scoped database lock, so two requests arriving at the
        // last free slot cannot both pass the count.
        //
        // Every endpoint counts, whatever its status. A disabled endpoint is
        // still a configured channel the workspace owns, and treating Disabled as
        // a free slot would let a workspace hold any number of them by turning
        // them off.
        return await _entitlements.ReserveAsync(
            tenantId,
            EntitlementKind.WebChatEndpoints,
            async token =>
            {
                ConcurrencyToken version = await _endpoints
                    .AddAsync(endpoint, token)
                    .ConfigureAwait(false);

                return new Versioned<WebChatEndpointView>(
                    WebChatEndpointView.From(endpoint, agent.Value.Name.Value, agent.Value.Status),
                    version);
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}
