using OmniAI.Application.Ai;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// What the runtime spends, and when it refuses to spend anything at all.
/// </summary>
/// <remarks>
/// <strong>Every assertion here is about money.</strong> The provider double
/// counts its own calls and the protection double counts the attempts it was
/// charged for, so each of these states a bound on what an anonymous stranger can
/// cost a workspace: a refused turn costs nothing, a repeated message costs one
/// attempt however many times it is sent, and a request with no establishable
/// origin costs nothing because it is not performed.
/// <para>
/// <strong>The ordering assertions are the subtle half.</strong> Whether a limit
/// refuses is easy; <em>where</em> it is consulted decides whether a visitor whose
/// browser retried a completed message is charged again. Those are asserted on
/// the recorded sequence of questions rather than left to a reading of the code.
/// </para>
/// </remarks>
public sealed class PublicChatProtectionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(37);

    // ---- The generation budget -------------------------------------------

    [Fact]
    public async Task A_refused_generation_calls_no_provider_and_stores_nothing()
    {
        // The mandatory invariant: a 429 means the provider call count is zero,
        // and there is no customer message pretending the turn was accepted.
        FakePublicChatProtection guard = new()
        {
            Generation = PublicChatDecision.Reject(Wait),
        };

        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        PublicChatRateLimitedException refusal =
            await Assert.ThrowsAsync<PublicChatRateLimitedException>(() =>
                harness.Send.HandleAsync(Harness.Sending(
                    session.Conversation.ConversationId,
                    session.AccessToken,
                    "m1",
                    "Are you open?")));

        Assert.Equal(Wait, refusal.RetryAfter);

        Assert.Equal(0, harness.Generator.Calls);
        Assert.Empty(harness.World.Turns);
        Assert.Empty(harness.World.Transcript(
            ConversationId.From(session.Conversation.ConversationId)));
    }

    [Fact]
    public async Task A_refused_request_never_reaches_the_generation_budget()
    {
        // The cheap boundary does its job: nothing further is consulted, so a
        // flood of doomed requests cannot drain the expensive budget either.
        FakePublicChatProtection guard = new()
        {
            Request = PublicChatDecision.Reject(Wait),
        };

        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        guard.Calls.Clear();

        await Assert.ThrowsAsync<PublicChatRateLimitedException>(() =>
            harness.Send.HandleAsync(Harness.Sending(
                session.Conversation.ConversationId,
                session.AccessToken,
                "m1",
                "Hello")));

        Assert.Equal(["request"], guard.Calls);
        Assert.Equal(0, guard.Generations);
        Assert.Equal(0, harness.Generator.Calls);
        Assert.Empty(harness.World.Turns);
    }

    [Fact]
    public async Task The_request_limit_is_consumed_before_the_credential_is_believed()
    {
        // Deliberate: the point of an anti-flood limit is to stop a request that
        // was going to be refused anyway from costing a database round trip. A
        // junk credential still counts against the network that sent it.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);

        await harness.StartAsync();

        guard.Calls.Clear();

        WebChatTurnResult? refused = await harness.Send.HandleAsync(Harness.Sending(
            Guid.CreateVersion7(),
            new string('a', 43),
            "m1",
            "Hello"));

        Assert.Null(refused);
        Assert.Equal(["request"], guard.Calls);
    }

    [Fact]
    public async Task The_generation_budget_is_consumed_after_both_idempotency_checks()
    {
        // The whole reason the budget sits at the bottom of the handler. Asked any
        // earlier and the duplicate below would have been charged for.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        guard.Calls.Clear();

        SendWebChatMessageCommand command = Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "one-message",
            "Hello");

        await harness.Send.HandleAsync(command);

        Assert.Equal(["request", "generation"], guard.Calls);
    }

    // ---- Duplicates ------------------------------------------------------

    [Fact]
    public async Task Repeating_one_message_spends_one_generation_however_often_it_is_sent()
    {
        // The mandatory regression. A browser that retried, a network that
        // duplicated, an impatient double click -- all the same logical turn, and
        // the expensive budget must see it once.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        SendWebChatMessageCommand command = Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "same-id",
            "Hello");

        for (int attempt = 0; attempt < 12; attempt++)
        {
            await harness.Send.HandleAsync(command);
        }

        Assert.Equal(1, guard.Generations);
        Assert.Equal(1, harness.Generator.Calls);

        // The cheap request counter did see all twelve, which is correct: they
        // were twelve HTTP requests. Only the expensive one is per logical turn.
        Assert.Equal(12, guard.Calls.Count(question => question == "request"));
        Assert.Equal(1, guard.Calls.Count(question => question == "generation"));
    }

    [Fact]
    public async Task Concurrent_duplicates_spend_one_generation_between_them()
    {
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        SendWebChatMessageCommand command = Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "same-id",
            "Hello");

        Task<WebChatTurnResult?>[] attempts =
        [
            Task.Run(() => harness.Send.HandleAsync(command)),
            Task.Run(() => harness.Send.HandleAsync(command)),
            Task.Run(() => harness.Send.HandleAsync(command)),
        ];

        foreach (Task<WebChatTurnResult?> attempt in attempts)
        {
            try
            {
                await attempt;
            }
            catch (WebChatTurnInProgressException)
            {
                // One of the racing copies is told its message is already being
                // answered. That is the contract, not a failure.
            }
        }

        Assert.Equal(1, guard.Generations);
        Assert.Equal(1, harness.Generator.Calls);
    }

    // ---- Failed provider attempts ----------------------------------------

    [Fact]
    public async Task A_failed_provider_attempt_still_costs_one_generation()
    {
        // Intentional, and documented. The consumption happens before the call and
        // is not refunded: an attacker able to make attempts fail -- by exhausting
        // an upstream, say -- would otherwise have found an unlimited free lane
        // through the safety budget. The workspace made an external attempt either
        // way.
        FakePublicChatProtection guard = new();

        Harness harness = Harness.Build(
            generator: RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()),
            protection: guard);

        StartedWebChatSession session = await harness.StartAsync();

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(() =>
            harness.Send.HandleAsync(Harness.Sending(
                session.Conversation.ConversationId,
                session.AccessToken,
                "m1",
                "Hello")));

        Assert.Equal(1, guard.Generations);
    }

    [Fact]
    public async Task Re_presenting_a_failed_message_costs_nothing_further()
    {
        // A failed turn is terminal. Re-presenting its id is refused before the
        // budget is reached, so a client retrying in a loop cannot drain a
        // workspace's allowance on a message that will never be answered.
        FakePublicChatProtection guard = new();

        Harness harness = Harness.Build(
            generator: RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()),
            protection: guard);

        StartedWebChatSession session = await harness.StartAsync();

        SendWebChatMessageCommand command = Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Hello");

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => harness.Send.HandleAsync(command));

        await Assert.ThrowsAsync<WebChatTurnAlreadyFailedException>(
            () => harness.Send.HandleAsync(command));

        Assert.Equal(1, guard.Generations);
    }

    // ---- The budget is charged to the right workspace --------------------

    [Fact]
    public async Task The_generation_is_charged_to_the_workspace_that_owns_the_conversation()
    {
        // The visitor supplies no workspace and could not be trusted with one. It
        // is read from the session, which is what stops one business's traffic
        // being counted against another's budget.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Hello"));

        Assert.Equal(harness.Workspace, guard.ChargedWorkspace);
        Assert.Equal(
            ConversationId.From(session.Conversation.ConversationId),
            guard.ChargedConversation);
    }

    // ---- Failing closed --------------------------------------------------

    [Fact]
    public async Task A_message_with_no_establishable_network_is_refused_and_costs_nothing()
    {
        // Unknown is not a bucket. An "unknown" counter would be the one unmetered
        // lane through the whole policy, so the write is refused instead.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        guard.Calls.Clear();

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(() =>
            harness.Send.HandleAsync(new SendWebChatMessageCommand(
                session.Conversation.ConversationId,
                session.AccessToken,
                "m1",
                "Hello",
                Client: null)));

        Assert.Empty(guard.Calls);
        Assert.Equal(0, harness.Generator.Calls);
        Assert.Empty(harness.World.Turns);
    }

    [Fact]
    public async Task An_unreachable_protection_store_refuses_the_write_rather_than_allowing_it()
    {
        // Fail closed. The platform could have answered perfectly well and
        // declined because it could not tell whether it should -- which is the
        // whole point, and the opposite of an in-memory fallback that would
        // silently stop protecting anything the moment the shared store struggled.
        FakePublicChatProtection guard = new()
        {
            Unreachable = () => new PublicChatProtectionUnavailableException("Redis is away."),
        };

        Harness harness = Harness.Build(protection: guard);

        // Started before the store went away, so there is a real conversation to
        // attempt a write against.
        guard.Unreachable = null;
        StartedWebChatSession session = await harness.StartAsync();
        guard.Unreachable = () => new PublicChatProtectionUnavailableException("Redis is away.");

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(() =>
            harness.Send.HandleAsync(Harness.Sending(
                session.Conversation.ConversationId,
                session.AccessToken,
                "m1",
                "Hello")));

        Assert.Equal(0, harness.Generator.Calls);
        Assert.Empty(harness.World.Turns);
    }

    // ---- Starting a conversation -----------------------------------------

    [Fact]
    public async Task A_refused_start_creates_no_conversation()
    {
        FakePublicChatProtection guard = new()
        {
            Start = PublicChatDecision.Reject(Wait),
        };

        Harness harness = Harness.Build(protection: guard);

        PublicChatRateLimitedException refusal =
            await Assert.ThrowsAsync<PublicChatRateLimitedException>(() =>
                harness.Start.StartAsync(
                    Harness.Starting(harness.Endpoint.PublicKey.Value)));

        Assert.Equal(Wait, refusal.RetryAfter);
        Assert.Empty(harness.World.Conversations);
    }

    [Fact]
    public async Task A_start_with_no_establishable_network_creates_no_conversation()
    {
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(() =>
            harness.Start.StartAsync(new StartWebChatConversationCommand(
                harness.Endpoint.PublicKey.Value,
                Client: null)));

        Assert.Empty(guard.Calls);
        Assert.Empty(harness.World.Conversations);
    }

    [Fact]
    public async Task An_unknown_key_is_not_found_without_consuming_a_start()
    {
        // A typo must not spend somebody's allowance, and the aggregate limit is
        // per endpoint -- so there is no endpoint to charge until one is found.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);

        StartedWebChatSession? nothing = await harness.Start.StartAsync(
            Harness.Starting(new string('z', 22)));

        Assert.Null(nothing);
        Assert.Empty(guard.Calls);
    }

    [Fact]
    public async Task A_withdrawn_endpoint_is_refused_before_a_start_is_consumed()
    {
        // Unavailability is not rate limiting. Charging a start against an endpoint
        // that cannot serve anybody would count refusals towards a limit meant to
        // bound conversations.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(activateEndpoint: false, protection: guard);

        await Assert.ThrowsAsync<WebChatUnavailableException>(() =>
            harness.Start.StartAsync(Harness.Starting(harness.Endpoint.PublicKey.Value)));

        Assert.Empty(guard.Calls);
        Assert.Empty(harness.World.Conversations);
    }

    // ---- Reads are not gated on the protection store ---------------------

    [Fact]
    public async Task Reading_a_transcript_needs_no_protection_store_at_all()
    {
        // The documented boundary. Reading a transcript creates nothing and costs
        // no provider call, so an unreachable counter store must not take somebody's
        // conversation away from them. Only the writes fail closed.
        FakePublicChatProtection guard = new();
        Harness harness = Harness.Build(protection: guard);
        StartedWebChatSession session = await harness.StartAsync();

        await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Hello"));

        guard.Unreachable = () => new PublicChatProtectionUnavailableException("Redis is away.");

        PublicChatMessagePage? page = await harness.List.HandleAsync(
            new ListWebChatMessagesQuery(
                session.Conversation.ConversationId,
                session.AccessToken,
                null,
                null));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }
}
