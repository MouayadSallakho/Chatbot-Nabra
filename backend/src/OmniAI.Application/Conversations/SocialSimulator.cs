using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

public sealed record SimulatorAgent(Guid Id, string Name);
public sealed record SimulatorAgentOption(Guid Id, string Name, string CustomerId);
public sealed record SimulatorInboundText(string? SenderId, string? EventId, string? Text);
public sealed record SimulatorMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);
public enum SimulatorScenario { Accept, Refuse, LoseConfirmation }
public sealed record SimulatorDeliveryIssue(Guid Id, string Sender, string Text, string Status);
public sealed record SimulatorMailbox(Guid? ConversationId, IReadOnlyList<SimulatorMessage> Messages,
    IReadOnlyList<SimulatorDeliveryIssue> DeliveryIssues);
public sealed record SimulatorAddress(TenantId Tenant, AiAgentId Agent, Guid Visitor, ChannelKind Channel = ChannelKind.Messenger);

public interface ISocialSimulatorStore
{
    Task<IReadOnlyList<SimulatorAgent>> AgentsAsync(TenantId tenant, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, string>> DeliveryStatusesAsync(TenantId tenant, ConversationId conversation, CancellationToken token);
    Task<ConversationId> ResolveAsync(SimulatorAddress address, DateTimeOffset now, CancellationToken token);
    Task<SimulatorMailbox> ReceiveAsync(SimulatorAddress address, CancellationToken token,
        SimulatorScenario scenario = SimulatorScenario.Accept, Guid? recover = null);
}

/// <summary>Runs authenticated test-customer turns through the same inbox workflow as other channels.</summary>
public sealed class SocialSimulator(TenantAuthorizer authorizer, ISocialSimulatorStore store,
    IAiAgentRepository agents, CustomerTurnRunner turns, TimeProvider clock)
{
    public async Task<IReadOnlyList<SimulatorAgentOption>?> AgentsAsync(Guid tenantId, CancellationToken token, ChannelKind channel = ChannelKind.Messenger)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanTestAsync(tenant, token).ConfigureAwait(false))
        {
            return null;
        }

        var visitor = authorizer.RequireCurrentUser().Value;
        var options = await store.AgentsAsync(tenant, token).ConfigureAwait(false);
        return [.. options.Select(agent => new SimulatorAgentOption(agent.Id, agent.Name, CustomerId(agent.Id, visitor, channel)))];
    }

    // A synthetic Page-scoped ID, stable across reloads without exposing an operator's user ID.
    public static string CustomerId(Guid page, Guid visitor, ChannelKind channel = ChannelKind.Messenger) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            channel == ChannelKind.Instagram ? $"instagram/{page:N}/{visitor:N}" : $"{page:N}/{visitor:N}")));

    public async Task<IReadOnlyDictionary<Guid, string>?> DeliveryStatusesAsync(Guid tenantId, Guid conversationId, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        return await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsView, token).ConfigureAwait(false)
            ? await store.DeliveryStatusesAsync(tenant, ConversationId.From(conversationId), token).ConfigureAwait(false)
            : null;
    }

    public async Task<SimulatorMailbox?> ReceiveAsync(Guid tenantId, Guid agentId, CancellationToken token,
        SimulatorScenario scenario = SimulatorScenario.Accept, Guid? recover = null, ChannelKind channel = ChannelKind.Messenger)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanTestAsync(tenant, token).ConfigureAwait(false))
        {
            return null;
        }

        return await store.ReceiveAsync(new SimulatorAddress(tenant, AiAgentId.From(agentId),
            authorizer.RequireCurrentUser().Value, channel), token, scenario, recover).ConfigureAwait(false);
    }

    public Task<SimulatorMailbox?> SendAsync(Guid tenantId, Guid agentId, Guid eventId, string? text, CancellationToken token) =>
        ApplyAsync(tenantId, agentId, [new SimulatorInboundText(null, eventId == Guid.Empty ? null : eventId.ToString("N"), text)],
            checkSender: false, ChannelKind.Messenger, SimulatorScenario.Accept, token);

    public Task<SimulatorMailbox?> SendPageAsync(Guid tenantId, Guid agentId, IReadOnlyList<SimulatorInboundText> messages,
        CancellationToken token, SimulatorScenario scenario = SimulatorScenario.Accept) =>
        ApplyAsync(tenantId, agentId, messages, checkSender: true, ChannelKind.Messenger, scenario, token);

    public Task<SimulatorMailbox?> SendInstagramAsync(Guid tenantId, Guid agentId, IReadOnlyList<SimulatorInboundText> messages, CancellationToken token) =>
        ApplyAsync(tenantId, agentId, messages, checkSender: true, ChannelKind.Instagram, SimulatorScenario.Accept, token);

    private async Task<SimulatorMailbox?> ApplyAsync(Guid tenantId, Guid agentId, IReadOnlyList<SimulatorInboundText> messages,
        bool checkSender, ChannelKind channel, SimulatorScenario scenario, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanTestAsync(tenant, token).ConfigureAwait(false))
        {
            return null;
        }

        var visitor = authorizer.RequireCurrentUser().Value;
        var customerId = CustomerId(agentId, visitor, channel);
        // Validate the entire batch before recording a turn. A retry after a runtime failure
        // can then replay every completed turn by its original message ID.
        foreach (var message in messages)
        {
            if (checkSender && !string.Equals(message.SenderId, customerId, StringComparison.Ordinal))
            {
                return null;
            }

            string content = message.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message.EventId) || message.EventId.Length > 512 ||
                content.Length is 0 or > 2000 || content.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            {
                throw new DomainValidationException("Provide a message ID and 1 to 2000 characters of text.")
                { Code = channel == ChannelKind.Instagram ? "instagram.simulator.invalid_message" : "messenger.simulator.invalid_message" };
            }
        }

        var agent = await agents.FindAsync(tenant, AiAgentId.From(agentId), token).ConfigureAwait(false);
        if (agent is null || agent.Value.Status != AiAgentStatus.Active)
        {
            return null;
        }

        var address = new SimulatorAddress(tenant, agent.Value.Id, visitor, channel);
        if (messages.Count > 0)
        {
            var conversation = await store.ResolveAsync(address, clock.GetUtcNow(), token).ConfigureAwait(false);
            foreach (var message in messages)
            {
                var key = ClientMessageId.Create(SocialInboundText.Key(channel, message.EventId!));
                await turns.RunAsync(new CustomerTurn(tenant, conversation, agent.Value, key, message.Text!.Trim(), clock.GetUtcNow()),
                    beforeGeneration: null, token).ConfigureAwait(false);
            }
        }

        return await store.ReceiveAsync(address, token, scenario).ConfigureAwait(false);
    }

    private async Task<bool> CanTestAsync(TenantId tenant, CancellationToken token) =>
        await authorizer.RequireAsync(tenant, PlatformPermissions.WorkspaceManage, token).ConfigureAwait(false)
        && await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsView, token).ConfigureAwait(false)
        && await authorizer.RequireAsync(tenant, PlatformPermissions.AiAgentsView, token).ConfigureAwait(false);
}
