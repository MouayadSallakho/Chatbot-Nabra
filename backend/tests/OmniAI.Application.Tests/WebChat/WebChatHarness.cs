using OmniAI.Application.Conversations;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Tests.Knowledge;
using OmniAI.Application.Tests.AiAgents;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Embedded;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// One website chat scenario, wired the way production wires it.
/// </summary>
/// <remarks>
/// <strong>Shared between the runtime suite and the protection suite, because
/// they are the same runtime.</strong> It was private to the runtime tests until
/// the protection tests needed exactly the same arrangement — an active agent, an
/// active endpoint, a provider that counts its calls, a lock that really excludes,
/// and now a protection layer a test can drive. Copying it would have produced two
/// harnesses that drifted until the two suites were exercising different objects,
/// which for a boundary this security-sensitive is the worst possible outcome.
/// <para>
/// <strong>Nothing here is a shortcut past a rule.</strong> Every collaborator is
/// either the real production type or a double that models its contract
/// faithfully: real token minting with real digests, a real access check with a
/// real clock, a real ordering-preserving message store. The doubles that are not
/// production types say so in their own documentation and each explains what it
/// does differently and why.
/// </para>
/// </remarks>
internal sealed class Harness
{
    /// <summary>The instant every scenario starts at.</summary>
    public static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The network every request appears to come from by default.
    /// </summary>
    /// <remarks>
    /// A fixed value, because these suites are about the runtime rather than about
    /// how a pseudonym is derived — that belongs to the Infrastructure suite that
    /// owns the HMAC. What matters here is that a real one is always supplied,
    /// which is what makes the tests that deliberately omit it meaningful.
    /// </remarks>
    public static readonly ClientNetworkKey Visitor = ClientNetworkKey.From("network-a");

    public required FakeWebChatWorld World { get; init; }

    public required InMemoryAiAgentRepository Agents { get; init; }

    public required RecordingGenerator Generator { get; init; }

    public required FakeConversationLock Lock { get; init; }

    public required FakePublicChatProtection Protection { get; init; }

    public required TestWebChatTokens Tokens { get; init; }

    public required StartWebChatConversationHandler Start { get; init; }

    public required SendWebChatMessageHandler Send { get; init; }

    /// <summary>
    /// The one orchestration every channel enters.
    /// </summary>
    /// <remarks>
    /// Exposed so the WhatsApp suite can drive the same instance website chat
    /// does. Two runners built from two arrangements could agree with each other
    /// while production disagreed with both.
    /// </remarks>
    public required CustomerTurnRunner Runner { get; init; }

    public required ListWebChatMessagesHandler List { get; init; }

    /// <summary>
    /// The embedded widget's doorway.
    /// </summary>
    /// <remarks>
    /// Built from the same collaborators as the hosted handlers, because it is the
    /// same runtime reached by a different transport. A separate arrangement would
    /// be a second world in which the two could agree while production disagreed.
    /// </remarks>
    public required EmbeddedWebChatHandler Embed { get; init; }

    public required WebChatEndpoint Endpoint { get; init; }

    public required AiAgent Agent { get; init; }

    /// <summary>
    /// Every realtime announcement this run made: a tenant and a conversation
    /// id, which is the whole payload the platform sends.
    /// </summary>
    public required RecordingRealtimeNotifier Announcer { get; init; }

    public required TenantId Workspace { get; init; }

    /// <summary>The clock every handler in this scenario reads.</summary>
    public required FixedTimeProvider Clock { get; init; }

    /// <summary>The limits this scenario was built with.</summary>
    public required PublicChatProtectionOptions Limits { get; init; }

    /// <summary>
    /// The embedding provider this scenario would spend money at.
    /// </summary>
    /// <remarks>
    /// Present in every scenario, including the ones with no knowledge at all,
    /// because "it was never called" is exactly what most of these tests need to
    /// assert.
    /// </remarks>
    public required RecordingEmbeddingGenerator Embeddings { get; init; }

    /// <summary>The knowledge this agent can draw on. Empty unless a test fills it.</summary>
    public required FakeKnowledgeSearchStore Knowledge { get; init; }

    public static Harness Build(
        RecordingGenerator? generator = null,
        AiAgentStatus agentStatus = AiAgentStatus.Active,
        bool activateEndpoint = true,
        ConversationContextPolicy? context = null,
        WebChatPolicy? policy = null,
        FakePublicChatProtection? protection = null,
        PublicChatProtectionOptions? limits = null,
        FixedTimeProvider? clock = null,
        FakeKnowledgeSearchStore? knowledge = null,
        RecordingEmbeddingGenerator? embeddings = null,
        KnowledgeRetrievalPolicy? retrieval = null)
    {
        TenantId workspace = TenantId.New();

        AiAgent agent = AiAgent.Create(
            workspace,
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Be helpful and concise."),
            Noon);

        if (agentStatus == AiAgentStatus.Active)
        {
            agent.Activate(Noon);
        }
        else if (agentStatus == AiAgentStatus.Disabled)
        {
            agent.Activate(Noon);
            agent.Disable(Noon);
        }

        FakeWebChatWorld world = new();
        world.AddAgent(agent.Id, agent.Name.Value, agent.Status);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            workspace,
            agent.Id,
            new TestWebChatPublicKeys().Create(),
            Noon);

        if (activateEndpoint)
        {
            endpoint.Activate(Noon);
        }

        world.AddEndpoint(endpoint);

        InMemoryAiAgentRepository agents = new();
        agents.Seed(agent);

        RecordingGenerator recorder = generator ?? RecordingGenerator.Replying();
        FakeConversationLock gate = new();
        TestWebChatTokens tokens = new();
        WebChatPolicy chatPolicy = policy ?? new WebChatPolicy();
        ConversationContextPolicy contextPolicy = context ?? new ConversationContextPolicy();
        RecordingRealtimeNotifier announcer = new();
        FakePublicChatProtection guard = protection ?? new FakePublicChatProtection();
        PublicChatProtectionOptions bounds = limits ?? new PublicChatProtectionOptions();
        FixedTimeProvider time = clock ?? new FixedTimeProvider(Noon);
        // The endpoint repository is passed as well as the runtime store: an
        // embedded session is re-checked against its endpoint's allowlist on every
        // turn, so revoking a website takes effect at once rather than when the
        // session happens to expire.
        PublicWebChatAccess access = new(world, world, tokens, time);

        FakeKnowledgeSearchStore library = knowledge ?? new FakeKnowledgeSearchStore();
        RecordingEmbeddingGenerator embedder = embeddings ?? new RecordingEmbeddingGenerator();

        KnowledgeRetriever retriever = new(
            library,
            embedder,
            retrieval ?? new KnowledgeRetrievalPolicy());

        // Built once and shared, because production builds one: the send handler
        // and every other channel resolve the same scoped instance.
        CustomerTurnRunner runner = new(
            world,
            world,
            recorder,
            gate,
            announcer,
            retriever,
            contextPolicy,
            time);

        return new Harness
        {
            World = world,
            Agents = agents,
            Generator = recorder,
            Lock = gate,
            Protection = guard,
            Tokens = tokens,
            Endpoint = endpoint,
            Agent = agent,
            Announcer = announcer,
            Workspace = workspace,
            Clock = time,
            Limits = bounds,
            Embeddings = embedder,
            Knowledge = library,
            Start = new StartWebChatConversationHandler(
                world,
                world,
                tokens,
                guard,
                bounds,
                time),
            Runner = runner,
            Send = new SendWebChatMessageHandler(
                access,
                world,
                agents,
                // The shared runner, built from exactly the pieces the send
                // handler used to hold itself. Website chat now enters the same
                // orchestration WhatsApp does, so these tests exercise the one
                // path rather than a copy of it.
                runner,
                guard,
                chatPolicy,
                time),
            List = new ListWebChatMessagesHandler(access, world, world, chatPolicy),
            Embed = new EmbeddedWebChatHandler(world, world, tokens, guard, bounds, time),
        };
    }

    /// <summary>
    /// A start command from a known network.
    /// </summary>
    /// <remarks>
    /// Written once so every test supplies a network identity by default, and so
    /// the tests that deliberately do not are visibly different from the rest
    /// rather than differing by an argument nobody notices.
    /// </remarks>
    public static StartWebChatConversationCommand Starting(string? publicKey) =>
        new(publicKey, Visitor);

    /// <summary>A send command from a known network.</summary>
    public static SendWebChatMessageCommand Sending(
        Guid conversationId,
        string? accessToken,
        string? clientMessageId,
        string? text) =>
        new(conversationId, accessToken, clientMessageId, text, Visitor);

    /// <summary>A send command from a widget embedded on a customer's website.</summary>
    public static SendWebChatMessageCommand SendingEmbedded(
        Guid conversationId,
        string? accessToken,
        string? clientMessageId,
        string? text) =>
        new(
            conversationId,
            accessToken,
            clientMessageId,
            text,
            Visitor,
            WebChatClientMode.Embedded);

    /// <summary>A transcript read from an embedded widget.</summary>
    public static ListWebChatMessagesQuery ListingEmbedded(
        Guid conversationId,
        string? accessToken) =>
        new(conversationId, accessToken, null, null, WebChatClientMode.Embedded);

    /// <summary>Approves one website on this scenario's endpoint.</summary>
    public void Allow(params string[] origins)
    {
        Endpoint.Configure(
            Endpoint.Widget,
            [.. origins.Select(WebChatOrigin.Create)],
            Clock.GetUtcNow());
    }

    /// <summary>Starts an embedded conversation from an approved website.</summary>
    public async Task<StartedEmbeddedConversation> StartEmbeddedAsync(string origin)
    {
        StartedEmbeddedConversation? started = await Embed.StartAsync(
            new StartEmbeddedConversationCommand(Endpoint.PublicKey.Value, origin, Visitor));

        Assert.NotNull(started);

        return started;
    }

    /// <summary>Starts a conversation on this scenario's endpoint.</summary>
    public async Task<StartedWebChatSession> StartAsync()
    {
        StartedWebChatSession? started = await Start.StartAsync(
            Starting(Endpoint.PublicKey.Value));

        Assert.NotNull(started);

        return started;
    }
}
