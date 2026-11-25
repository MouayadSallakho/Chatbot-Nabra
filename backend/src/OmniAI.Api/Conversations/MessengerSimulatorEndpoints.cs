using OmniAI.Api.Security;
using OmniAI.Application.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.Conversations;

internal static class MessengerSimulatorEndpoints
{
    internal sealed record SimulatedText(Guid EventId, string? Text);

    public static void MapMessengerSimulator(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId:guid}/messenger-simulator")
            .RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>().WithTags("Messenger simulator");
        group.MapGet("", async (Guid tenantId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.AgentsAsync(tenantId, token).ConfigureAwait(false) is { } agents ? Results.Ok(agents) : Results.NotFound())
            .Produces<IReadOnlyList<SimulatorAgentOption>>();
        group.MapGet("/conversations/{conversationId:guid}/deliveries", async (Guid tenantId, Guid conversationId,
            SocialSimulator simulator, CancellationToken token) =>
            await simulator.DeliveryStatusesAsync(tenantId, conversationId, token).ConfigureAwait(false) is { } statuses
                ? Results.Ok(statuses) : Results.NotFound())
            .Produces<IReadOnlyDictionary<Guid, string>>();
        group.MapPost("/{agentId:guid}/receive", async (Guid tenantId, Guid agentId, SimulatorScenario? scenario, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, ReadScenario(scenario)).ConfigureAwait(false) is { } box ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/deliveries/{messageId:guid}/recover", async (Guid tenantId, Guid agentId,
            Guid messageId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, recover: messageId).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/page-events", async (Guid tenantId, Guid agentId, MessengerPageEnvelope request, SimulatorScenario? scenario,
            SocialSimulator simulator, CancellationToken token) =>
            await simulator.SendPageAsync(tenantId, agentId, request.ReadTexts(agentId), token, ReadScenario(scenario)).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPost("/{agentId:guid}", async (Guid tenantId, Guid agentId, SimulatedText request, SocialSimulator simulator, CancellationToken token) =>
            await simulator.SendAsync(tenantId, agentId, request.EventId, request.Text, token).ConfigureAwait(false) is { } box ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static SimulatorScenario ReadScenario(SimulatorScenario? scenario) =>
        scenario is null ? SimulatorScenario.Accept : Enum.IsDefined(scenario.Value) ? scenario.Value :
        throw new DomainValidationException("Select a supported delivery test.")
        { Code = "messenger.simulator.invalid_scenario" };
}
