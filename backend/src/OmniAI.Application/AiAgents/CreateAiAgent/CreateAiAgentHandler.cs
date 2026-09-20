using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.CreateAiAgent;

/// <summary>
/// Creates an AI agent under a tenant.
/// </summary>
/// <remarks>
/// A plain class with constructor dependencies, resolved by name at the
/// composition root. There is still no mediator, command bus or pipeline: a
/// dispatcher would add indirection, reflection and a registration convention
/// while removing nothing, and it would hide a call graph that is currently
/// readable in one hop.
/// </remarks>
public sealed class CreateAiAgentHandler(
    IWorkspaceEntitlementService entitlements,
    ITenantRepository tenants,
    IAiAgentRepository agents,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Creates the agent and returns its read model and version.</summary>
    /// <exception cref="Domain.DomainValidationException">
    /// The tenant id is empty, or the name or instructions break a Domain rule.
    /// Raised by the value objects, not re-implemented here.
    /// </exception>
    /// <exception cref="TenantNotFoundException">No such tenant.</exception>
    public async Task<Versioned<AiAgentView>> HandleAsync(
        CreateAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Input first, existence second. A malformed name is wrong however the
        // rest of the world looks and can be answered without touching the
        // database, whereas whether a tenant exists is a question about stored
        // state. So a request that is both malformed and aimed at a missing
        // tenant is reported as malformed, which is the ordinary precedence.
        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentName name = AiAgentName.Create(command.Name);
        AgentInstructions instructions = AgentInstructions.Create(command.Instructions);

        // Authorisation before existence, and a non-member is answered the same
        // way a missing tenant is. A caller who does not belong to this tenant is
        // told the tenant is not there -- which, for them, is the only true thing
        // that can be said about it. If this check ran after the existence check,
        // a non-member would learn that somebody else's workspace exists by the
        // order in which they were refused.
        //
        // A member without ai-agents.create is told which permission they need,
        // because they have already proved they belong here and the workspace's
        // existence is not news to them.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsCreate, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        // The ordinary answer to "create an agent under a tenant that is not
        // there". Without it the insert would fail on the foreign key, and a
        // routine mistake would surface as an unexplained server error instead of
        // a plain "no such tenant". The foreign key still exists and still
        // refuses the row -- it is the defence that survives this check being
        // raced -- but it is no longer what a normal caller meets.
        //
        // Still reachable despite the membership check above: a membership row
        // can outlive its tenant only if something deleted the tenant behind the
        // foreign key, and this is what would report that honestly rather than
        // crashing.
        if (await _tenants.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }


        // Permission said this person may create one; the plan says whether the
        // workspace may hold another. Two different questions, asked in that
        // order, and neither answers the other -- a member with the permission is
        // still refused when the workspace is full, and no amount of role editing
        // changes that.
        //
        // The count and the insert happen together under a workspace-scoped
        // database lock, so two requests arriving at the last free slot cannot
        // both pass the count.
        return await _entitlements.ReserveAsync(
            tenantId,
            EntitlementKind.AiAgents,
            async token =>
            {
                // Identity, initial status and the update mark come from the
                // aggregate. The use case supplies only the instant, and does not
                // read the clock itself: TimeProvider is injected so the same code
                // is deterministic under test and correct in production.
                AiAgent agent =
                    AiAgent.Create(tenantId, name, instructions, _clock.GetUtcNow());

                // The version the stored agent now has. Returned so the caller can
                // make a conditional write immediately, without reading the agent
                // back first.
                ConcurrencyToken version =
                    await _agents.AddAsync(agent, token).ConfigureAwait(false);

                return new Versioned<AiAgentView>(AiAgentView.From(agent), version);
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}
