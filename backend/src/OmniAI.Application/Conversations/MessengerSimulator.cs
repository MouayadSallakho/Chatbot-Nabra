using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

public sealed record SimulatorAgent(Guid Id, string Name);
public sealed record SimulatorMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);
public sealed record SimulatorMailbox(Guid? ConversationId, IReadOnlyList<SimulatorMessage> Messages);
public sealed record SimulatorAddress(TenantId Tenant, AiAgentId Agent, Guid Visitor);

public interface IMessengerSimulatorStore
{
    Task<IReadOnlyList<SimulatorAgent>> AgentsAsync(TenantId tenant, CancellationToken token);
    Task<ConversationId> ResolveAsync(SimulatorAddress address, DateTimeOffset now, CancellationToken token);
    Task<SimulatorMailbox> ReceiveAsync(SimulatorAddress address, CancellationToken token);
}

/// <summary>A simulated Page per agent and a Page-scoped visitor per signed-in tester.</summary>
public sealed class MessengerSimulator(TenantAuthorizer authorizer, IMessengerSimulatorStore store,
    IAiAgentRepository agents, CustomerTurnRunner turns, TimeProvider clock)
{
    public async Task<IReadOnlyList<SimulatorAgent>?> AgentsAsync(Guid tenantId, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        return await CanTestAsync(tenant, token).ConfigureAwait(false)
            ? await store.AgentsAsync(tenant, token).ConfigureAwait(false) : null;
    }

    public async Task<SimulatorMailbox?> ReceiveAsync(Guid tenantId, Guid agentId, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanTestAsync(tenant, token).ConfigureAwait(false))
        {
            return null;
        }

        return await store.ReceiveAsync(new SimulatorAddress(tenant, AiAgentId.From(agentId),
            authorizer.RequireCurrentUser().Value), token).ConfigureAwait(false);
    }

    public async Task<SimulatorMailbox?> SendAsync(Guid tenantId, Guid agentId, Guid eventId, string? text, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanTestAsync(tenant, token).ConfigureAwait(false))
        {
            return null;
        }

        string content = text?.Trim() ?? string.Empty;
        if (eventId == Guid.Empty || content.Length is 0 or > 2000 || content.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
        {
            throw new DomainValidationException("Provide an event ID and 1 to 2000 characters of text.")
            { Code = "messenger.simulator.invalid_message" };
        }

        var agent = await agents.FindAsync(tenant, AiAgentId.From(agentId), token).ConfigureAwait(false);
        if (agent is null || agent.Value.Status != AiAgentStatus.Active)
        {
            return null;
        }

        var address = new SimulatorAddress(tenant, agent.Value.Id, authorizer.RequireCurrentUser().Value);
        var now = clock.GetUtcNow();
        var conversation = await store.ResolveAsync(address, now, token).ConfigureAwait(false);
        var key = ClientMessageId.Create(SocialInboundText.Key(ChannelKind.Messenger, eventId.ToString("N")));
        await turns.RunAsync(new CustomerTurn(tenant, conversation, agent.Value, key, content, now),
            beforeGeneration: null, token).ConfigureAwait(false);

        return await store.ReceiveAsync(address, token).ConfigureAwait(false);
    }

    private async Task<bool> CanTestAsync(TenantId tenant, CancellationToken token) =>
        await authorizer.RequireAsync(tenant, PlatformPermissions.WorkspaceManage, token).ConfigureAwait(false)
        && await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsView, token).ConfigureAwait(false)
        && await authorizer.RequireAsync(tenant, PlatformPermissions.AiAgentsView, token).ConfigureAwait(false);
}
