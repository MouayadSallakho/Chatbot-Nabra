using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// Phase 37.5a, row 18: an agent's channels on the agent reads, and only the
/// kinds the reader may see -- decided once per request, before any channel is
/// read.
/// </summary>
public sealed class AiAgentChannelReadTests
{
    private static readonly UserId Reader = UserId.New();
    private static readonly TenantId Workspace = TenantId.New();
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false, AiAgentChannelKinds.None)]
    [InlineData(true, false, AiAgentChannelKinds.WebChat)]
    [InlineData(false, true, AiAgentChannelKinds.WhatsApp)]
    [InlineData(true, true, AiAgentChannelKinds.WebChat | AiAgentChannelKinds.WhatsApp)]
    public async Task The_list_asks_only_for_the_kinds_the_reader_may_see(bool webChat, bool whatsApp, AiAgentChannelKinds asked)
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        InMemoryAiAgentChannelReader channels = BothChannels(agent);

        AiAgentPage page = await new ListAiAgentsHandler(agents, Holding(webChat, whatsApp), channels)
            .HandleAsync(new ListAiAgentsQuery(Workspace.Value, null, null));

        // A reader with neither permission never causes a channel read at all.
        AiAgentChannelKinds[] expectedRequests = asked == AiAgentChannelKinds.None ? [] : [asked];
        Assert.Equal(expectedRequests, channels.Requests);
        Assert.Equal(Expected(webChat, whatsApp), Assert.Single(page.Items).Channels.Select(channel => channel.Kind));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task The_detail_shows_the_same_kinds_as_the_list(bool webChat, bool whatsApp)
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();

        Versioned<AiAgentView>? found = await new GetAiAgentByIdHandler(agents, Holding(webChat, whatsApp), BothChannels(agent))
            .HandleAsync(new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value));

        Assert.Equal(Expected(webChat, whatsApp), found!.Value.Channels.Select(channel => channel.Kind));
    }

    [Fact]
    public async Task The_refusals_are_unchanged()
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        InMemoryAiAgentChannelReader channels = BothChannels(agent);
        TenantAuthorizer withoutView = TestAuthority.With(Reader, Workspace.Value, PlatformPermissions.WebsiteChatView);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => new ListAiAgentsHandler(agents, withoutView, channels)
            .HandleAsync(new ListAiAgentsQuery(Workspace.Value, null, null)));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => new GetAiAgentByIdHandler(agents, withoutView, channels)
            .HandleAsync(new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value)));

        await Assert.ThrowsAsync<global::OmniAI.Application.Tenants.TenantNotFoundException>(() => new ListAiAgentsHandler(agents, TestAuthority.Outsider(Reader), channels)
            .HandleAsync(new ListAiAgentsQuery(Workspace.Value, null, null)));
        Assert.Null(await new GetAiAgentByIdHandler(agents, TestAuthority.Outsider(Reader), channels)
            .HandleAsync(new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value)));

        Assert.Empty(channels.Requests);
    }

    [Fact]
    public async Task Another_workspaces_channels_never_attach_to_an_agent_here()
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        InMemoryAiAgentChannelReader channels = new InMemoryAiAgentChannelReader()
            .With(TenantId.New(), agent.Id, new AiAgentChannelView(AiAgentChannelKind.WebChat, Guid.CreateVersion7(), "Active"));

        AiAgentPage page = await new ListAiAgentsHandler(agents, Holding(true, true), channels)
            .HandleAsync(new ListAiAgentsQuery(Workspace.Value, null, null));

        Assert.Empty(Assert.Single(page.Items).Channels);
    }

    private static (InMemoryAiAgentRepository Agents, AiAgent Agent) Seed()
    {
        AiAgent agent = AiAgent.Create(
            Workspace,
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Answer briefly."),
            Created);

        return (new InMemoryAiAgentRepository().Seed(agent), agent);
    }

    private static InMemoryAiAgentChannelReader BothChannels(AiAgent agent) =>
        new InMemoryAiAgentChannelReader()
            .With(Workspace, agent.Id, new AiAgentChannelView(AiAgentChannelKind.WebChat, Guid.CreateVersion7(), "Active"))
            .With(Workspace, agent.Id, new AiAgentChannelView(AiAgentChannelKind.WhatsApp, Guid.CreateVersion7(), "Draft"));

    private static TenantAuthorizer Holding(bool webChat, bool whatsApp)
    {
        List<PermissionCode> held = [PlatformPermissions.AiAgentsView];

        if (webChat)
        {
            held.Add(PlatformPermissions.WebsiteChatView);
        }

        if (whatsApp)
        {
            held.Add(PlatformPermissions.WhatsAppView);
        }

        return TestAuthority.With(Reader, Workspace.Value, [.. held]);
    }

    private static List<AiAgentChannelKind> Expected(bool webChat, bool whatsApp)
    {
        List<AiAgentChannelKind> kinds = [];

        if (webChat)
        {
            kinds.Add(AiAgentChannelKind.WebChat);
        }

        if (whatsApp)
        {
            kinds.Add(AiAgentChannelKind.WhatsApp);
        }

        return kinds;
    }
}
