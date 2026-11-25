using System.Text.Json.Serialization;
using OmniAI.Domain;
using OmniAI.Api.Security;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;

namespace OmniAI.Api.Conversations;

internal static class InstagramSimulatorEndpoints
{
    internal sealed record OwnershipChange([property: JsonRequired] bool ExternalOwner, [property: JsonRequired] int Version);

    public static void MapInstagramSimulator(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId:guid}/instagram-simulator")
            .RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>().WithTags("Instagram simulator");
        group.MapGet("", async (Guid tenantId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.AgentsAsync(tenantId, token, ChannelKind.Instagram).ConfigureAwait(false) is { } agents
                ? Results.Ok(agents) : Results.NotFound()).Produces<IReadOnlyList<SimulatorAgentOption>>();
        group.MapGet("/{agentId:guid}/mailbox", async (Guid tenantId, Guid agentId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, channel: ChannelKind.Instagram, inspectOnly: true).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/receive", async (Guid tenantId, Guid agentId, SimulatorScenario? scenario, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, ReadScenario(scenario), channel: ChannelKind.Instagram).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/deliveries/{messageId:guid}/recover", async (Guid tenantId, Guid agentId, Guid messageId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, recover: messageId, channel: ChannelKind.Instagram).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/handover-events", async (Guid tenantId, Guid agentId, InstagramHandoverEnvelope request,
            SocialSimulator simulator, CancellationToken token) =>
            await simulator.ApplyInstagramHandoverAsync(tenantId, agentId, request.Read(agentId), token).ConfigureAwait(false) is { } result
                ? Results.Ok(result) : Results.NotFound()).Produces<HandoverResult>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPost("/{agentId:guid}/ownership", async (Guid tenantId, Guid agentId, OwnershipChange request, SocialSimulator simulator, CancellationToken token) =>
            await simulator.SetInstagramOwnershipAsync(tenantId, agentId, new SimulatorOwnership(request.ExternalOwner, request.Version), token).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>();
        group.MapGet("/conversations/{conversationId:guid}/state", async (Guid tenantId, Guid conversationId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.InstagramStateAsync(tenantId, conversationId, token).ConfigureAwait(false) is { } state
                ? Results.Ok(state) : Results.NotFound()).Produces<InstagramInboxState>();
        group.MapPost("/{agentId:guid}/direct-events", async (Guid tenantId, Guid agentId, InstagramDirectEnvelope request, SimulatorScenario? scenario,
            SocialSimulator simulator, CancellationToken token) =>
            await simulator.SendInstagramAsync(tenantId, agentId, request.ReadTexts(agentId), token, ReadScenario(scenario)).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
    }
    private static SimulatorScenario ReadScenario(SimulatorScenario? scenario) =>
        scenario is null ? SimulatorScenario.Accept : Enum.IsDefined(scenario.Value) ? scenario.Value :
        throw new DomainValidationException("Select a supported delivery test.") { Code = "instagram.simulator.invalid_scenario" };
}
