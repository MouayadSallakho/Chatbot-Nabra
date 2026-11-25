using OmniAI.Api.Security;
using OmniAI.Application.Conversations;

namespace OmniAI.Api.Conversations;

internal static class MessengerSimulatorEndpoints
{
    internal sealed record SimulatedText(Guid EventId, string? Text);

    public static void MapMessengerSimulator(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId:guid}/messenger-simulator")
            .RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>().WithTags("Messenger simulator");
        group.MapGet("", async (Guid tenantId, MessengerSimulator simulator, CancellationToken token) =>
            await simulator.AgentsAsync(tenantId, token).ConfigureAwait(false) is { } agents ? Results.Ok(agents) : Results.NotFound())
            .Produces<IReadOnlyList<SimulatorAgentOption>>();
        group.MapPost("/{agentId:guid}/receive", async (Guid tenantId, Guid agentId, MessengerSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token).ConfigureAwait(false) is { } box ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/page-events", async (Guid tenantId, Guid agentId, MessengerPageEnvelope request,
            MessengerSimulator simulator, CancellationToken token) =>
            await simulator.SendPageAsync(tenantId, agentId, request.ReadTexts(agentId), token).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapPost("/{agentId:guid}", async (Guid tenantId, Guid agentId, SimulatedText request, MessengerSimulator simulator, CancellationToken token) =>
            await simulator.SendAsync(tenantId, agentId, request.EventId, request.Text, token).ConfigureAwait(false) is { } box ? Results.Ok(box) : Results.NotFound())
            .Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
