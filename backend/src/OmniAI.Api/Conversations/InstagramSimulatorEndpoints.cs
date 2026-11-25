using OmniAI.Api.Security;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;

namespace OmniAI.Api.Conversations;

internal static class InstagramSimulatorEndpoints
{
    public static void MapInstagramSimulator(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants/{tenantId:guid}/instagram-simulator")
            .RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>().WithTags("Instagram simulator");
        group.MapGet("", async (Guid tenantId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.AgentsAsync(tenantId, token, ChannelKind.Instagram).ConfigureAwait(false) is { } agents
                ? Results.Ok(agents) : Results.NotFound()).Produces<IReadOnlyList<SimulatorAgentOption>>();
        group.MapGet("/{agentId:guid}/mailbox", async (Guid tenantId, Guid agentId, SocialSimulator simulator, CancellationToken token) =>
            await simulator.ReceiveAsync(tenantId, agentId, token, channel: ChannelKind.Instagram).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>();
        group.MapPost("/{agentId:guid}/direct-events", async (Guid tenantId, Guid agentId, InstagramDirectEnvelope request,
            SocialSimulator simulator, CancellationToken token) =>
            await simulator.SendInstagramAsync(tenantId, agentId, request.ReadTexts(agentId), token).ConfigureAwait(false) is { } box
                ? Results.Ok(box) : Results.NotFound()).Produces<SimulatorMailbox>().ProducesProblem(StatusCodes.Status400BadRequest);
    }
}
