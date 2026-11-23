using Npgsql;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Phase 37.4 reads in PostgreSQL: a fixed number of statements per page, the
/// branches of "needs a person" an HTTP test cannot reach, and the schema of
/// migrations M4, M5 and M6. Phase 38.7a adds the reason each row carries, and
/// proves it against the filter on the same rows.
/// </summary>
public sealed class InboxReadModelTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Viewer = UserId.New();

    // ---- No N+1 -------------------------------------------------------------

    [Theory]
    [InlineData(ConversationListFilter.All)]
    [InlineData(ConversationListFilter.NeedsPerson)]
    public async Task A_page_costs_the_same_statements_whatever_its_length(ConversationListFilter filter)
    {
        int small = await StatementsForPageAsync(conversations: 2, filter);
        int large = await StatementsForPageAsync(conversations: 9, filter);

        Assert.Equal(small, large);

        // Count, page, customers' names and customers' channels. Since Phase 38.7a
        // the assignee and takeover ids come with the page itself, and naming
        // them asks only when there is somebody to name (nobody here); no
        // WhatsApp conversation, so no number lookup.
        Assert.Equal(4, large);
    }

    [Fact]
    public async Task Transcript_events_cost_the_same_statements_however_many_there_are()
    {
        (Tenant tenant, Conversation few) = await ConversationWithFailuresAsync(failures: 1);
        (_, Conversation many) = await ConversationWithFailuresAsync(failures: 6, tenant);

        Assert.Equal(
            await StatementsForEventsAsync(few.Id),
            await StatementsForEventsAsync(many.Id));
    }

    [Fact]
    public async Task Search_matches_names_and_older_messages_before_paging_and_respects_workspace_and_view()
    {
        Tenant tenant = await NewTenantAsync();
        Tenant other = await NewTenantAsync();
        Conversation named = WaitingOnAPerson(tenant);
        Conversation historical = WaitingOnAPerson(tenant);
        Conversation foreign = WaitingOnAPerson(other);
        Conversation nearMiss = WaitingOnAPerson(tenant);
        const string needle = "طلب A!_%";
        foreach (Conversation conversation in new[] { historical, foreign })
        {
            conversation.AddTextMessage(MessageSender.Customer, needle, Start.AddMinutes(4));
            conversation.AddTextMessage(MessageSender.Customer, "Later message", Start.AddMinutes(5));
        }

        nearMiss.AddTextMessage(MessageSender.Customer, "طلب A!xx", Start.AddMinutes(4));
        await AddAsync(named, historical, foreign, nearMiss);
        await using OmniAiDbContext context = database.CreateContext();
        Customer customer = (await context.Customers.FindAsync(named.CustomerId))!;
        customer.Rename(CustomerDisplayName.Create(needle), Start.AddDays(1));
        await context.SaveChangesAsync();
        ConversationRepository repository = new(context);
        const string search = "طلب a!_%";
        ConversationPage first = await repository.ListAsync(tenant.Id, 1, 1, ConversationListFilter.All, Viewer, search);
        ConversationPage second = await repository.ListAsync(tenant.Id, 2, 1, ConversationListFilter.All, Viewer, search);
        ConversationPage mine = await repository.ListAsync(tenant.Id, 1, 10, ConversationListFilter.Mine, Viewer, search);

        var window = new InboxFilters(ActiveFrom: Start.AddMinutes(4), ActiveBefore: Start.AddMinutes(5));
        var activity = await repository.ListAsync(tenant.Id, 1, 10, ConversationListFilter.All, Viewer, criteria: window);
        Assert.Equal(nearMiss.Id.Value, Assert.Single(activity.Items).Id);
        Assert.Equal(2, first.Total);
        Assert.Equal(historical.Id.Value, Assert.Single(first.Items).Id);
        Assert.Equal(named.Id.Value, Assert.Single(second.Items).Id);
        Assert.Empty(mine.Items);
        Assert.Equal(0, mine.Total);
    }

    // ---- Needs a person -------------------------------------------------------

    [Fact]
    public async Task A_closed_conversation_never_needs_a_person()
    {
        Tenant tenant = await NewTenantAsync();

        Conversation open = WaitingOnAPerson(tenant);
        Conversation closed = WaitingOnAPerson(tenant);
        closed.Close(Start.AddMinutes(5));

        await AddAsync(open, closed);

        await using OmniAiDbContext context = database.CreateContext();
        ConversationPage page = await new ConversationRepository(context)
            .ListAsync(tenant.Id, 1, 20, ConversationListFilter.NeedsPerson, Viewer);

        Assert.Equal(open.Id.Value, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task A_failed_turn_needs_a_person_only_while_it_is_the_last_word()
    {
        (Tenant tenant, Conversation conversation) = await ConversationWithFailuresAsync(failures: 1);

        Assert.Equal(1, await NeedsPersonTotalAsync(tenant));

        // The AI answers a later message: the customer is no longer waiting.
        await AnswerAsync(conversation);

        Assert.Equal(0, await NeedsPersonTotalAsync(tenant));
    }

    [Fact]
    public async Task A_message_left_unanswered_by_a_return_to_the_ai_needs_a_person_until_closed()
    {
        // Revision 1: handled by the AI, the customer wrote last, and that turn
        // completed without a reply (a person was handling it at the time).
        Tenant tenant = await NewTenantAsync();

        (Conversation open, ConversationTurn openTurn) = LeftUnanswered(tenant);
        (Conversation closed, ConversationTurn closedTurn) = LeftUnanswered(tenant);
        closed.Close(Start.AddMinutes(10));

        await using (OmniAiDbContext context = database.CreateContext())
        {
            context.AddOwner(open);
            context.AddOwner(closed);
            context.Conversations.AddRange(open, closed);
            await context.SaveChangesAsync();
            context.ConversationTurns.AddRange(openTurn, closedTurn);
            await context.SaveChangesAsync();
        }

        await using OmniAiDbContext read = database.CreateContext();
        ConversationPage page = await new ConversationRepository(read)
            .ListAsync(tenant.Id, 1, 20, ConversationListFilter.NeedsPerson, Viewer);

        ConversationSummary only = Assert.Single(page.Items);

        Assert.Equal(open.Id.Value, only.Id);
        Assert.Equal(ConversationHandling.Ai, only.Handling);
        Assert.Equal(1, page.Total);
    }

    // ---- Why it needs a person (Phase 38.7a, BA-7) -----------------------------

    [Fact]
    public async Task Each_row_carries_its_reason_and_the_flag_agrees_with_the_filter()
    {
        Tenant tenant = await NewTenantAsync();

        Conversation personHandling = WaitingOnAPerson(tenant);
        Conversation closed = WaitingOnAPerson(tenant);
        closed.Close(Start.AddMinutes(5));
        Conversation answered = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        answered.AddTextMessage(MessageSender.Customer, "Hello", Start.AddMinutes(1));
        answered.AddTextMessage(MessageSender.AiAgent, "Hi.", Start.AddMinutes(1).AddSeconds(2));
        Conversation empty = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        (Conversation returned, ConversationTurn returnedTurn) = LeftUnanswered(tenant);
        (Conversation returnedThenClosed, ConversationTurn closedTurn) = LeftUnanswered(tenant, "closed-later");
        returnedThenClosed.Close(Start.AddMinutes(10));

        // The AI's turn failed, and then a person took it over: the person wins.
        Conversation failedThenTaken = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        Message asked = failedThenTaken.AddTextMessage(MessageSender.Customer, "Refund?", Start.AddMinutes(1));
        ConversationTurn failedTurn = ConversationTurn.Begin(
            failedThenTaken.Id, ClientMessageId.Create("failed-then-taken"), asked.Id, Start.AddMinutes(1));
        failedTurn.Fail(Start.AddMinutes(1).AddSeconds(5));
        failedThenTaken.TakeOverByHuman(Start.AddMinutes(2));

        await using (OmniAiDbContext context = database.CreateContext())
        {
            foreach (Conversation conversation in new[] { personHandling, closed, answered, empty, returned, returnedThenClosed, failedThenTaken })
            {
                context.AddOwner(conversation);
                context.Conversations.Add(conversation);
            }

            await context.SaveChangesAsync();
            context.ConversationTurns.AddRange(returnedTurn, closedTurn, failedTurn);
            await context.SaveChangesAsync();
        }

        (Tenant _, Conversation failed) = await ConversationWithFailuresAsync(failures: 1, tenant);

        Dictionary<Guid, NeedsPersonReason?> expected = new()
        {
            [personHandling.Id.Value] = NeedsPersonReason.PersonHandling,
            [closed.Id.Value] = null,
            [answered.Id.Value] = null,
            [empty.Id.Value] = null,
            [returned.Id.Value] = NeedsPersonReason.ReturnedUnanswered,
            [returnedThenClosed.Id.Value] = null,
            [failedThenTaken.Id.Value] = NeedsPersonReason.PersonHandling,
            [failed.Id.Value] = NeedsPersonReason.AiTurnFailed,
        };

        await using OmniAiDbContext read = database.CreateContext();
        ConversationRepository repository = new(read);

        ConversationPage all = await repository.ListAsync(tenant.Id, 1, 50, ConversationListFilter.All, Viewer);
        ConversationPage filtered = await repository.ListAsync(tenant.Id, 1, 50, ConversationListFilter.NeedsPerson, Viewer);

        Assert.Equal(expected.Count, all.Total);
        Assert.All(all.Items, item => Assert.Equal(expected[item.Id], item.NeedsPersonReason));
        Assert.All(all.Items, item => Assert.Equal(item.NeedsPersonReason is not null, item.NeedsPerson));

        // The flag and the filter, on the same rows.
        Assert.True(
            filtered.Items.Select(item => item.Id).ToHashSet()
                .SetEquals(all.Items.Where(item => item.NeedsPerson).Select(item => item.Id)),
            "The flag and the filter disagree.");
        Assert.Equal(all.Items.Count(item => item.NeedsPerson), filtered.Total);
        Assert.All(filtered.Items, item => Assert.Equal(expected[item.Id], item.NeedsPersonReason));

        // The detail says the same as the row, for every one of them.
        foreach (ConversationSummary item in all.Items)
        {
            ConversationDetail detail = (await repository.FindAsync(tenant.Id, ConversationId.From(item.Id), Viewer))!.Value;

            Assert.Equal(item.NeedsPersonReason, detail.NeedsPersonReason);
            Assert.Null(detail.TakenOverBy);
        }
    }

    [Fact]
    public async Task A_takeover_from_before_its_events_existed_names_nobody()
    {
        // TakeOverByHuman alone records no event, as before Phase 37.4.
        Tenant tenant = await NewTenantAsync();
        Conversation conversation = WaitingOnAPerson(tenant);

        await AddAsync(conversation);

        await using OmniAiDbContext read = database.CreateContext();
        ConversationSummary row = Assert.Single(
            (await new ConversationRepository(read).ListAsync(tenant.Id, 1, 20, ConversationListFilter.All, Viewer)).Items);

        Assert.Equal(ConversationHandling.Human, row.Handling);
        Assert.Null(row.TakenOverBy);
    }

    // ---- Schema (M4, M5, M6) --------------------------------------------------

    [Theory]
    [InlineData("ix_conversations_tenant_handling_activity", "(tenant_id, handling, last_activity_at)")]
    [InlineData("ix_conversation_handling_events_conversation", "(conversation_id, after_sequence)")]
    [InlineData("ix_conversation_reads_member", "(user_id, tenant_id)")]
    [InlineData("pk_conversation_reads", "(conversation_id, user_id)")]
    public async Task The_new_indexes_exist(string name, string columns)
    {
        string? definition = await ScalarAsync(
            $"SELECT indexdef FROM pg_indexes WHERE indexname = '{name}'");

        Assert.NotNull(definition);
        Assert.Contains(columns, definition, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fk_conversation_handling_events_actor_member", "(actor_user_id, tenant_id) REFERENCES tenant_memberships(user_id, tenant_id) ON DELETE RESTRICT")]
    [InlineData("fk_conversation_handling_events_conversation", "ON DELETE CASCADE")]
    [InlineData("fk_conversation_reads_member", "(user_id, tenant_id) REFERENCES tenant_memberships(user_id, tenant_id) ON DELETE CASCADE")]
    [InlineData("fk_conversation_reads_conversation", "ON DELETE CASCADE")]
    [InlineData("ck_conversation_handling_events_after_sequence", "after_sequence >= 0")]
    [InlineData("ck_conversation_reads_last_read_sequence", "last_read_sequence >= 0")]
    public async Task The_new_constraints_exist(string name, string fragment)
    {
        string? definition = await ScalarAsync(
            $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{name}'");

        Assert.NotNull(definition);
        Assert.Contains(fragment, definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handling_events_keep_no_index_on_their_tenant_or_actor_alone()
    {
        // Rows are always reached through their conversation; an index nobody
        // reads is write cost for nothing.
        string? extra = await ScalarAsync(
            """
            SELECT string_agg(indexname, ',') FROM pg_indexes
            WHERE tablename = 'conversation_handling_events'
              AND indexname NOT IN ('PK_conversation_handling_events', 'ix_conversation_handling_events_conversation')
            """);

        Assert.Null(extra);
    }

    // ---- Helpers ----------------------------------------------------------------

    private async Task<int> StatementsForPageAsync(int conversations, ConversationListFilter filter)
    {
        Tenant tenant = await NewTenantAsync();

        await AddAsync([.. Enumerable.Range(0, conversations).Select(_ => WaitingOnAPerson(tenant))]);

        RecordedSql recorded = new();

        await using OmniAiDbContext context = database.CreateContext(recorded);
        ConversationPage page = await new ConversationRepository(context)
            .ListAsync(tenant.Id, 1, 50, filter, Viewer);

        Assert.Equal(conversations, page.Items.Count);
        Assert.All(page.Items, item => Assert.NotNull(item.Customer));
        Assert.All(page.Items, item => Assert.True(item.Unread));

        return recorded.Statements.Count;
    }

    private async Task<int> StatementsForEventsAsync(ConversationId conversationId)
    {
        RecordedSql recorded = new();

        await using OmniAiDbContext context = database.CreateContext(recorded);
        IReadOnlyList<TranscriptEvent> events = await new ConversationRepository(context)
            .ListTranscriptEventsAsync(conversationId, long.MinValue, long.MaxValue);

        Assert.NotEmpty(events);
        Assert.All(events, entry => Assert.Equal(TranscriptEventKind.AiTurnFailed, entry.Kind));

        return recorded.Statements.Count;
    }

    private async Task<int> NeedsPersonTotalAsync(Tenant tenant)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return (await new ConversationRepository(context)
            .ListAsync(tenant.Id, 1, 20, ConversationListFilter.NeedsPerson, Viewer)).Total;
    }

    /// <summary>Handled by a person, and the customer wrote last.</summary>
    private static Conversation WaitingOnAPerson(Tenant tenant)
    {
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);

        conversation.AddTextMessage(MessageSender.Customer, "Hello", Start.AddMinutes(1));
        conversation.TakeOverByHuman(Start.AddMinutes(2));
        conversation.AddTextMessage(MessageSender.Customer, "Anybody?", Start.AddMinutes(3));

        return conversation;
    }

    /// <summary>
    /// Taken over, the customer writes (its turn completes without a reply), and
    /// it is handed back to the AI with nobody having answered.
    /// </summary>
    private static (Conversation Conversation, ConversationTurn Turn) LeftUnanswered(
        Tenant tenant,
        string clientMessageId = "left-unanswered")
    {
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);

        conversation.AddTextMessage(MessageSender.Customer, "Hello", Start.AddMinutes(1));
        conversation.AddTextMessage(MessageSender.AiAgent, "Hi.", Start.AddMinutes(1).AddSeconds(2));
        conversation.TakeOverByHuman(Start.AddMinutes(2));
        Message asked = conversation.AddTextMessage(MessageSender.Customer, "A person, please", Start.AddMinutes(3));
        conversation.ReturnToAi(Start.AddMinutes(4));

        ConversationTurn turn = ConversationTurn.Begin(
            conversation.Id,
            ClientMessageId.Create(clientMessageId),
            asked.Id,
            Start.AddMinutes(3));
        turn.CompleteWithoutReply(Start.AddMinutes(3).AddSeconds(1));

        return (conversation, turn);
    }

    /// <summary>The AI is answering; each failure is a customer message whose turn failed.</summary>
    private async Task<(Tenant Tenant, Conversation Conversation)> ConversationWithFailuresAsync(
        int failures,
        Tenant? tenant = null)
    {
        tenant ??= await NewTenantAsync();

        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        List<ConversationTurn> turns = [];

        for (int index = 0; index < failures; index++)
        {
            Message asked = conversation.AddTextMessage(MessageSender.Customer, $"Question {index}", Start.AddMinutes(index + 1));
            ConversationTurn turn = ConversationTurn.Begin(
                conversation.Id,
                ClientMessageId.Create($"q{index}"),
                asked.Id,
                Start.AddMinutes(index + 1));
            turn.Fail(Start.AddMinutes(index + 1).AddSeconds(5));
            turns.Add(turn);
        }

        await using OmniAiDbContext context = database.CreateContext();
        context.AddOwner(conversation);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();
        context.ConversationTurns.AddRange(turns);
        await context.SaveChangesAsync();

        return (tenant, conversation);
    }

    private async Task AnswerAsync(Conversation conversation)
    {
        await using OmniAiDbContext context = database.CreateContext();
        Conversation tracked = await context.Conversations.FindAsync(conversation.Id) ?? throw new InvalidOperationException();

        tracked.AddTextMessage(MessageSender.Customer, "Hello again", Start.AddHours(1));
        tracked.AddTextMessage(MessageSender.AiAgent, "The answer.", Start.AddHours(1).AddSeconds(2));

        await context.SaveChangesAsync();
    }

    private async Task<Tenant> NewTenantAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Inbox"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return tenant;
    }

    private async Task AddAsync(params Conversation[] conversations)
    {
        await using OmniAiDbContext context = database.CreateContext();

        foreach (Conversation conversation in conversations)
        {
            context.AddOwner(conversation);
            context.Conversations.Add(conversation);
        }

        await context.SaveChangesAsync();
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(database.ConnectionString);

        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);

        object? value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? null : value.ToString();
    }
}
