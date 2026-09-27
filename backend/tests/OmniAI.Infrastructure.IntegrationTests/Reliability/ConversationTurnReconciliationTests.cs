using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Infrastructure.IntegrationTests.Analytics;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reliability;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reliability;

/// <summary>
/// Phase 35: a conversation turn a crash left <c>Generating</c> is settled, once,
/// without calling anybody.
/// </summary>
/// <remarks>
/// Phase 34 killed an API mid-turn and found the turn still Generating after every
/// other record had been reconciled: one stale row per crashed turn, and a resend
/// of the same client message id answered "in progress" for ever. These tests are
/// the durable half of that scenario, against real PostgreSQL.
/// </remarks>
public sealed class ConversationTurnReconciliationTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task A_turn_abandoned_mid_generation_is_failed_and_its_customer_message_is_kept()
    {
        Conversation conversation = await ConversationAsync();
        ConversationTurn abandoned = await GeneratingTurnAsync(conversation, Noon.AddHours(-1));

        int settled = await ReconcileAsync(Noon);

        Assert.True(settled >= 1);

        ConversationTurn stored = await LoadAsync(abandoned.Id);

        Assert.Equal(ConversationTurnStatus.Failed, stored.Status);
        Assert.Null(stored.ReplyMessageId);
        Assert.Equal(Noon, stored.UpdatedAt);

        // What the customer said is a fact; settling the turn does not unsay it.
        await using OmniAiDbContext context = database.CreateContext();

        Assert.True(await context.Set<Message>().AnyAsync(message => message.Id == abandoned.CustomerMessageId));
        Assert.False(await context.Set<Message>().AnyAsync(
            message => message.ConversationId == conversation.Id && message.Sender == MessageSender.AiAgent));
    }

    [Fact]
    public async Task A_turn_younger_than_the_threshold_is_left_generating()
    {
        Conversation conversation = await ConversationAsync();
        ConversationTurn running = await GeneratingTurnAsync(conversation, Noon.AddMinutes(-2));

        await ReconcileAsync(Noon);

        Assert.Equal(ConversationTurnStatus.Generating, (await LoadAsync(running.Id)).Status);
    }

    [Fact]
    public async Task Settled_turns_never_move_and_a_second_pass_changes_nothing()
    {
        Conversation conversation = await ConversationAsync();
        ConversationTurn abandoned = await GeneratingTurnAsync(conversation, Noon.AddHours(-2));
        ConversationTurn completed = await CompletedTurnAsync(conversation, Noon.AddHours(-2));

        await ReconcileAsync(Noon);
        await ReconcileAsync(Noon.AddHours(1));

        ConversationTurn failed = await LoadAsync(abandoned.Id);

        // The second pass did not touch it: updated_at is still the first pass's.
        Assert.Equal(ConversationTurnStatus.Failed, failed.Status);
        Assert.Equal(Noon, failed.UpdatedAt);

        ConversationTurn untouched = await LoadAsync(completed.Id);

        Assert.Equal(ConversationTurnStatus.Completed, untouched.Status);
        Assert.Equal(completed.ReplyMessageId, untouched.ReplyMessageId);
        Assert.Equal(Noon.AddHours(-2), untouched.UpdatedAt);
    }

    [Fact]
    public async Task One_pass_settles_at_most_one_batch()
    {
        Conversation conversation = await ConversationAsync();

        // Far older than anything the other tests leave behind, so the batch is
        // exactly these, whatever order the class runs in.
        for (int i = 0; i < 5; i++)
        {
            await GeneratingTurnAsync(conversation, Noon.AddDays(-30).AddMinutes(i));
        }

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(2, await new ConversationTurnReconciler(context).ReconcileAsync(Noon.AddDays(-29), Noon, 2));
    }

    /// <remarks>
    /// Two instances running the reconciler at the same moment: SKIP LOCKED gives
    /// them disjoint candidates and the repeated status predicate stops a second
    /// transition, so every stale turn is settled exactly once.
    /// </remarks>
    [Fact]
    public async Task Two_instances_reconciling_at_once_settle_each_turn_exactly_once()
    {
        Conversation conversation = await ConversationAsync();
        DateTimeOffset startedAt = Noon.AddDays(-60);

        for (int i = 0; i < 20; i++)
        {
            await GeneratingTurnAsync(conversation, startedAt.AddSeconds(i));
        }

        DateTimeOffset cutoff = startedAt.AddDays(1);

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        int[] settled = await Task.WhenAll(
            new ConversationTurnReconciler(a).ReconcileAsync(cutoff, Noon, 100),
            new ConversationTurnReconciler(b).ReconcileAsync(cutoff, Noon, 100));

        Assert.Equal(20, settled.Sum());

        await using OmniAiDbContext check = database.CreateContext();

        Assert.Equal(0, await check.ConversationTurns.CountAsync(
            turn => turn.ConversationId == conversation.Id && turn.Status == ConversationTurnStatus.Generating));
    }

    [Fact]
    public void The_threshold_cannot_be_shorter_than_a_turn_can_legitimately_take()
    {
        TimeSpan providerTimeout = TimeSpan.FromSeconds(30);
        TimeSpan indexingBound = TimeSpan.FromMinutes(7);

        ReliabilityOptions tooShort = new() { ConversationTurnStaleAfter = TimeSpan.FromMinutes(5) };

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => tooShort.Validate(providerTimeout, indexingBound));

        Assert.Contains(nameof(ReliabilityOptions.ConversationTurnStaleAfter), refused.Message, StringComparison.Ordinal);

        new ReliabilityOptions().Validate(providerTimeout, indexingBound);
    }

    private async Task<int> ReconcileAsync(DateTimeOffset now)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new ConversationTurnReconciler(context).ReconcileAsync(now - StaleAfter, now, 1000);
    }

    private async Task<Conversation> ConversationAsync()
    {
        AnalyticsWorld world = await new AnalyticsWorld(database).StartAsync();
        AiAgent agent = await world.AgentAsync("Turns", Noon.AddDays(-90));

        return await world.WebChatAsync(agent, Noon.AddDays(-90));
    }

    private async Task<ConversationTurn> GeneratingTurnAsync(Conversation conversation, DateTimeOffset at) =>
        await TurnAsync(conversation, at, complete: false);

    private async Task<ConversationTurn> CompletedTurnAsync(Conversation conversation, DateTimeOffset at) =>
        await TurnAsync(conversation, at, complete: true);

    private async Task<ConversationTurn> TurnAsync(Conversation conversation, DateTimeOffset at, bool complete)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Conversation tracked = await context.Conversations
            .Include(item => item.Messages)
            .SingleAsync(item => item.Id == conversation.Id);

        Message customer = tracked.AddTextMessage(MessageSender.Customer, "A question.", at);

        ConversationTurn turn = ConversationTurn.Begin(
            conversation.Id,
            ClientMessageId.Create($"c{Guid.NewGuid():N}"[..24]),
            customer.Id,
            at);

        if (complete)
        {
            Message reply = tracked.AddTextMessage(MessageSender.AiAgent, "An answer.", at);

            turn.Complete(reply.Id, at);
        }

        context.ConversationTurns.Add(turn);

        await context.SaveChangesAsync();

        return turn;
    }

    private async Task<ConversationTurn> LoadAsync(ConversationTurnId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.ConversationTurns.AsNoTracking().SingleAsync(turn => turn.Id == id);
    }
}
