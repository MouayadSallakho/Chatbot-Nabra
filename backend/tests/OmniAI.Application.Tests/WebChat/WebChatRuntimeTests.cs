using OmniAI.Application.Ai;
using OmniAI.Application.Conversations;
using OmniAI.Application.Tests.AiAgents;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// The conversation runtime: what it stores, what it spends, and what it refuses.
/// </summary>
/// <remarks>
/// <strong>Every one of these is about money or privacy.</strong> A duplicate that
/// bought a second reply, two messages that interleaved, a customer message lost
/// to a provider outage, or a credential that opened somebody else's
/// conversation — those are the failures this suite exists to make impossible,
/// and each is asserted against a provider double that counts its own calls.
/// </remarks>
public sealed class WebChatRuntimeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Everything one scenario needs, wired the way production wires it.</summary>
    private sealed class Harness
    {
        public required FakeWebChatWorld World { get; init; }
        public required InMemoryAiAgentRepository Agents { get; init; }
        public required RecordingGenerator Generator { get; init; }
        public required FakeConversationLock Lock { get; init; }
        public required TestWebChatTokens Tokens { get; init; }
        public required StartWebChatConversationHandler Start { get; init; }
        public required SendWebChatMessageHandler Send { get; init; }
        public required ListWebChatMessagesHandler List { get; init; }
        public required WebChatEndpoint Endpoint { get; init; }
        public required AiAgent Agent { get; init; }
        public required TenantId Workspace { get; init; }
    }

    private static Harness Build(
        RecordingGenerator? generator = null,
        AiAgentStatus agentStatus = AiAgentStatus.Active,
        bool activateEndpoint = true,
        ConversationContextPolicy? context = null,
        WebChatPolicy? policy = null)
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
        PublicWebChatAccess access = new(world, tokens);
        FakeTimeProvider clock = new(Noon);

        return new Harness
        {
            World = world,
            Agents = agents,
            Generator = recorder,
            Lock = gate,
            Tokens = tokens,
            Endpoint = endpoint,
            Agent = agent,
            Workspace = workspace,
            Start = new StartWebChatConversationHandler(world, world, tokens, clock),
            Send = new SendWebChatMessageHandler(
                access,
                world,
                world,
                world,
                agents,
                recorder,
                gate,
                contextPolicy,
                chatPolicy,
                clock),
            List = new ListWebChatMessagesHandler(access, world, chatPolicy),
        };
    }

    private static async Task<StartedWebChatSession> StartAsync(Harness harness)
    {
        StartedWebChatSession? started = await harness.Start.StartAsync(
            new StartWebChatConversationCommand(harness.Endpoint.PublicKey.Value));

        Assert.NotNull(started);

        return started;
    }

    // ---- Provider success -----------------------------------------------

    [Fact]
    public async Task A_visitor_message_is_stored_answered_and_both_are_kept()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        WebChatTurnResult? turn = await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Do you deliver on Sundays?"));

        Assert.NotNull(turn);
        Assert.Equal("Do you deliver on Sundays?", turn.CustomerMessage.Text);
        Assert.Equal("Customer", turn.CustomerMessage.Sender);
        Assert.Equal("The answer.", turn.Reply.Text);
        Assert.Equal("AiAgent", turn.Reply.Sender);

        Assert.Equal(1, harness.Generator.Calls);

        Assert.Equal(
            [(MessageSender.Customer, "Do you deliver on Sundays?"), (MessageSender.AiAgent, "The answer.")],
            harness.World.Transcript(ConversationId.From(session.Conversation.ConversationId)));
    }

    [Fact]
    public async Task The_provider_receives_the_agents_current_instructions()
    {
        // Read at the moment of the turn, not snapshotted when the conversation
        // began -- a workspace that corrects its agent expects the correction to
        // take effect from the next reply.
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello"));

        Assert.Equal("Be helpful and concise.", harness.Generator.Requests[0].Instructions);

        harness.Agent.UpdateInstructions(
            AgentInstructions.Create("Answer only in French."),
            Noon.AddHours(1));

        await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m2", "Again"));

        Assert.Equal("Answer only in French.", harness.Generator.Requests[1].Instructions);
    }

    [Fact]
    public async Task History_reaches_the_provider_in_ingestion_order_with_mapped_roles()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m1", "First question"));

        await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m2", "Second question"));

        IReadOnlyList<AiTurn> second = harness.Generator.Requests[1].Turns;

        Assert.Equal(
            [
                (AiTurnRole.User, "First question"),
                (AiTurnRole.Assistant, "The answer."),
                (AiTurnRole.User, "Second question"),
            ],
            second.Select(turn => (turn.Role, turn.Text)));
    }

    [Fact]
    public async Task Out_of_order_timestamps_do_not_reorder_the_history_sent_to_the_provider()
    {
        // The Phase 3 invariant, still holding through the runtime. Ingestion
        // order is the platform's order; SentAt is display only and is allowed to
        // be non-chronological, because in a distributed omnichannel system it is.
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        harness.World.SeedMessage(conversation, MessageSender.Customer, "Oldest ingested", Noon);
        harness.World.SeedMessage(
            conversation, MessageSender.AiAgent, "Ingested second", Noon.AddHours(-5));
        harness.World.SeedMessage(
            conversation, MessageSender.Customer, "Ingested third", Noon.AddHours(-9));

        await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Newest"));

        Assert.Equal(
            ["Oldest ingested", "Ingested second", "Ingested third", "Newest"],
            harness.Generator.Requests[0].Turns.Select(turn => turn.Text));
    }

    // ---- Idempotency -----------------------------------------------------

    [Fact]
    public async Task Repeating_a_message_returns_the_first_answer_and_buys_no_second_one()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        SendWebChatMessageCommand command = new(
            session.Conversation.ConversationId, session.AccessToken, "same-id", "Hello");

        WebChatTurnResult? first = await harness.Send.HandleAsync(command);
        WebChatTurnResult? repeat = await harness.Send.HandleAsync(command);

        Assert.NotNull(first);
        Assert.NotNull(repeat);

        // The same stored rows, not a fresh pair that happens to read alike.
        Assert.Equal(first.CustomerMessage.Id, repeat.CustomerMessage.Id);
        Assert.Equal(first.Reply.Id, repeat.Reply.Id);

        Assert.Equal(1, harness.Generator.Calls);
        Assert.Single(harness.World.Turns);

        Assert.Equal(2, harness.World.Transcript(
            ConversationId.From(session.Conversation.ConversationId)).Count);
    }

    [Fact]
    public async Task Concurrent_duplicates_produce_exactly_one_turn_and_one_provider_call()
    {
        // The double-click and the racing retry. Only one of these may reach the
        // provider; the other must recognise that its work is already done or
        // already happening.
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        SendWebChatMessageCommand command = new(
            session.Conversation.ConversationId, session.AccessToken, "same-id", "Hello");

        Task<WebChatTurnResult?>[] attempts =
        [
            Task.Run(() => harness.Send.HandleAsync(command)),
            Task.Run(() => harness.Send.HandleAsync(command)),
            Task.Run(() => harness.Send.HandleAsync(command)),
        ];

        int succeeded = 0;
        int refused = 0;

        foreach (Task<WebChatTurnResult?> attempt in attempts)
        {
            try
            {
                Assert.NotNull(await attempt);
                succeeded++;
            }
            catch (WebChatTurnInProgressException)
            {
                refused++;
            }
        }

        Assert.Equal(3, succeeded + refused);
        Assert.True(succeeded >= 1);

        Assert.Equal(1, harness.Generator.Calls);
        Assert.Single(harness.World.Turns);
    }

    [Fact]
    public async Task A_failed_message_is_not_retried_under_the_same_id()
    {
        // Terminal on purpose. Re-presenting a failed id must not spend money
        // again -- a genuine retry is a new message with a new id.
        Harness harness = Build(RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()));
        StartedWebChatSession session = await StartAsync(harness);

        SendWebChatMessageCommand command = new(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello");

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => harness.Send.HandleAsync(command));

        await Assert.ThrowsAsync<WebChatTurnAlreadyFailedException>(
            () => harness.Send.HandleAsync(command));

        Assert.Equal(1, harness.Generator.Calls);
    }

    // ---- Serialisation ---------------------------------------------------

    [Fact]
    public async Task Two_different_messages_to_one_conversation_are_answered_one_at_a_time()
    {
        // Not merely "both succeed": no two generations for one conversation may
        // ever be in flight together, or both answer a history neither of them
        // will have when they finish.
        TaskCompletionSource gate = new();
        Harness harness = Build(RecordingGenerator.Gated(gate));
        StartedWebChatSession session = await StartAsync(harness);

        Task<WebChatTurnResult?> first = Task.Run(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m1", "First")));

        Task<WebChatTurnResult?> second = Task.Run(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m2", "Second")));

        gate.SetResult();

        Assert.NotNull(await first);
        Assert.NotNull(await second);

        Assert.Equal(1, harness.Lock.MaxConcurrentHolders);
        Assert.Equal(2, harness.Generator.Calls);

        // Both customer messages stored exactly once, and the transcript reads as
        // two complete exchanges rather than two questions then two answers.
        IReadOnlyList<(MessageSender Sender, string Text)> transcript =
            harness.World.Transcript(ConversationId.From(session.Conversation.ConversationId));

        Assert.Equal(4, transcript.Count);
        Assert.Equal(MessageSender.Customer, transcript[0].Sender);
        Assert.Equal(MessageSender.AiAgent, transcript[1].Sender);
        Assert.Equal(MessageSender.Customer, transcript[2].Sender);
        Assert.Equal(MessageSender.AiAgent, transcript[3].Sender);

        Assert.Contains(transcript, entry => entry.Text == "First");
        Assert.Contains(transcript, entry => entry.Text == "Second");
    }

    [Fact]
    public async Task Different_conversations_are_not_serialised_against_each_other()
    {
        // The lock's scope is one conversation. Serialising the deployment would
        // make every workspace's chat as slow as the slowest reply anywhere.
        TaskCompletionSource gate = new();
        Harness harness = Build(RecordingGenerator.Gated(gate));

        StartedWebChatSession first = await StartAsync(harness);
        StartedWebChatSession second = await StartAsync(harness);

        Task<WebChatTurnResult?> a = Task.Run(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                first.Conversation.ConversationId, first.AccessToken, "m1", "Hello")));

        Task<WebChatTurnResult?> b = Task.Run(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                second.Conversation.ConversationId, second.AccessToken, "m1", "Hello")));

        // Both reach the provider before either is allowed to finish, which is
        // only possible if neither is waiting on the other's lock.
        await WaitForAsync(() => harness.Generator.Calls == 2);

        gate.SetResult();

        Assert.NotNull(await a);
        Assert.NotNull(await b);
        Assert.Equal(2, harness.Lock.MaxConcurrentHolders);
    }

    // ---- Provider failure ------------------------------------------------

    public static TheoryData<Func<Exception>> ProviderFailures => new()
    {
        () => new AiRuntimeUnavailableException(),
        () => new AiRuntimeTimeoutException(),
        () => new AiResponseInvalidException(),
    };

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task A_provider_failure_keeps_the_customer_message_and_invents_no_reply(
        Func<Exception> fault)
    {
        Harness harness = Build(RecordingGenerator.Failing(fault));
        StartedWebChatSession session = await StartAsync(harness);
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m1", "Are you there?")));

        // What the customer said survives. Nothing fabricates an answer, and no
        // placeholder appears in anybody's transcript.
        Assert.Equal(
            [(MessageSender.Customer, "Are you there?")],
            harness.World.Transcript(conversation));

        ConversationTurn turn = Assert.Single(harness.World.Turns);
        Assert.Equal(ConversationTurnStatus.Failed, turn.Status);
        Assert.Null(turn.ReplyMessageId);

        // Exactly one attempt. Nothing retried a paid request on its own.
        Assert.Equal(1, harness.Generator.Calls);
    }

    // ---- Service state ---------------------------------------------------

    [Fact]
    public async Task A_draft_endpoint_serves_nobody()
    {
        Harness harness = Build(activateEndpoint: false);

        await Assert.ThrowsAsync<WebChatUnavailableException>(() => harness.Start.StartAsync(
            new StartWebChatConversationCommand(harness.Endpoint.PublicKey.Value)));

        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task An_endpoint_bound_to_a_draft_agent_serves_nobody()
    {
        Harness harness = Build(agentStatus: AiAgentStatus.Draft);

        await Assert.ThrowsAsync<WebChatUnavailableException>(() => harness.Start.StartAsync(
            new StartWebChatConversationCommand(harness.Endpoint.PublicKey.Value)));
    }

    [Fact]
    public async Task An_endpoint_bound_to_a_disabled_agent_serves_nobody()
    {
        Harness harness = Build(agentStatus: AiAgentStatus.Disabled);

        await Assert.ThrowsAsync<WebChatUnavailableException>(() => harness.Start.StartAsync(
            new StartWebChatConversationCommand(harness.Endpoint.PublicKey.Value)));
    }

    [Fact]
    public async Task Disabling_the_agent_mid_conversation_stops_the_next_reply()
    {
        // Checked on every turn, not once at the start. An agent switched off must
        // stop answering the public immediately, not when its conversations end.
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        Assert.NotNull(await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello")));

        harness.Agent.Disable(Noon.AddHours(1));

        await Assert.ThrowsAsync<WebChatUnavailableException>(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m2", "Still there?")));

        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task Disabling_the_endpoint_stops_new_turns_but_not_reading_the_transcript()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        Assert.NotNull(await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello")));

        harness.Endpoint.Disable(Noon.AddHours(1));

        await Assert.ThrowsAsync<WebChatUnavailableException>(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m2", "Anyone?")));

        // The history a visitor already has is theirs. Withdrawing a channel is a
        // service decision, not a retention one.
        PublicChatMessagePage? page = await harness.List.HandleAsync(
            new ListWebChatMessagesQuery(
                session.Conversation.ConversationId, session.AccessToken, null, null));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    // ---- Access isolation ------------------------------------------------

    [Fact]
    public async Task One_visitor_cannot_read_or_write_another_visitors_conversation()
    {
        // The core privacy property. Visitor A holds a real credential, and it is
        // worth nothing against conversation B.
        Harness harness = Build();

        StartedWebChatSession a = await StartAsync(harness);
        StartedWebChatSession b = await StartAsync(harness);

        Assert.Null(await harness.List.HandleAsync(new ListWebChatMessagesQuery(
            b.Conversation.ConversationId, a.AccessToken, null, null)));

        Assert.Null(await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            b.Conversation.ConversationId, a.AccessToken, "m1", "Reading your mail")));

        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task Knowing_a_conversation_id_is_not_enough()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        Assert.Null(await harness.List.HandleAsync(new ListWebChatMessagesQuery(
            session.Conversation.ConversationId, null, null, null)));

        Assert.Null(await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, null, "m1", "Hello")));

        // A well-formed but wrong credential is refused identically.
        string invented = new TestWebChatTokens().Create().Token;

        Assert.Null(await harness.Send.HandleAsync(new SendWebChatMessageCommand(
            session.Conversation.ConversationId, invented, "m1", "Hello")));

        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task An_unknown_conversation_and_a_wrong_credential_are_indistinguishable()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        Assert.Null(await harness.List.HandleAsync(new ListWebChatMessagesQuery(
            Guid.CreateVersion7(), session.AccessToken, null, null)));
    }

    // ---- Input bounds ----------------------------------------------------

    [Fact]
    public async Task An_over_long_message_is_refused_rather_than_truncated()
    {
        Harness harness = Build(policy: new WebChatPolicy { MaxIncomingMessageLength = 20 });
        StartedWebChatSession session = await StartAsync(harness);

        DomainValidationException failure =
            await Assert.ThrowsAsync<DomainValidationException>(() => harness.Send.HandleAsync(
                new SendWebChatMessageCommand(
                    session.Conversation.ConversationId,
                    session.AccessToken,
                    "m1",
                    new string('x', 21))));

        Assert.Contains("not truncated", failure.Message, StringComparison.Ordinal);
        Assert.Empty(harness.World.Transcript(
            ConversationId.From(session.Conversation.ConversationId)));
        Assert.Equal(0, harness.Generator.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_message_is_refused(string? text)
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        await Assert.ThrowsAsync<DomainValidationException>(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, "m1", text)));

        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_missing_client_message_id_is_refused_before_anything_is_stored()
    {
        Harness harness = Build();
        StartedWebChatSession session = await StartAsync(harness);

        await Assert.ThrowsAsync<DomainValidationException>(() => harness.Send.HandleAsync(
            new SendWebChatMessageCommand(
                session.Conversation.ConversationId, session.AccessToken, null, "Hello")));

        Assert.Empty(harness.World.Turns);
    }

    // ---- The public face -------------------------------------------------

    [Fact]
    public async Task The_public_page_is_told_the_agent_name_and_nothing_else()
    {
        Harness harness = Build();

        PublicWebChatView? view = await harness.Start.GetAsync(
            new GetPublicWebChatQuery(harness.Endpoint.PublicKey.Value));

        Assert.NotNull(view);
        Assert.Equal("Support", view.AgentName);
        Assert.True(view.Available);

        // The type has exactly two properties. Anything about the workspace, the
        // agent's configuration or its identity would have to be added here
        // deliberately, which is the point of asserting the shape.
        Assert.Equal(2, typeof(PublicWebChatView).GetProperties().Length);
    }

    [Fact]
    public async Task An_unknown_or_malformed_public_key_is_not_found()
    {
        Harness harness = Build();

        Assert.Null(await harness.Start.GetAsync(new GetPublicWebChatQuery("not-a-real-key")));
        Assert.Null(await harness.Start.GetAsync(new GetPublicWebChatQuery(null)));
        Assert.Null(await harness.Start.GetAsync(
            new GetPublicWebChatQuery(new TestWebChatPublicKeys().Create().Value)));
    }

    [Fact]
    public async Task A_withdrawn_endpoint_still_renders_a_page_but_reports_itself_unavailable()
    {
        // A visitor followed a real link and is told the chat is off, rather than
        // that their link is broken -- while learning nothing about why.
        Harness harness = Build(activateEndpoint: false);

        PublicWebChatView? view = await harness.Start.GetAsync(
            new GetPublicWebChatQuery(harness.Endpoint.PublicKey.Value));

        Assert.NotNull(view);
        Assert.False(view.Available);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected state was not reached in time.");
    }
}

/// <summary>A clock the test sets.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
