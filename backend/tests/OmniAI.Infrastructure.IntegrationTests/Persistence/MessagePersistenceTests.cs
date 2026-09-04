using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

public sealed class MessagePersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset T1 = new(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 4, 1, 10, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 4, 1, 10, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_message_round_trips_with_every_field_intact()
    {
        Conversation conversation = await NewConversationAsync();
        Message sent = conversation.AddTextMessage(MessageSender.Customer, "Hello there", T1);
        MessageId originalId = sent.Id;

        await SaveAsync(conversation);

        Conversation reloaded = await LoadAsync(conversation.Id);
        Message message = Assert.Single(reloaded.Messages);

        Assert.Equal(originalId, message.Id);
        Assert.Equal(conversation.Id, message.ConversationId);
        Assert.Equal(MessageSender.Customer, message.Sender);
        Assert.Equal(MessageContentType.Text, message.ContentType);
        Assert.Equal("Hello there", message.Text);
        Assert.Equal(T1, message.SentAt);
    }

    [Theory]
    [InlineData(MessageSender.Customer)]
    [InlineData(MessageSender.AiAgent)]
    [InlineData(MessageSender.HumanAgent)]
    public async Task Every_text_sender_round_trips_and_is_stored_by_name(MessageSender sender)
    {
        Conversation conversation = await NewConversationAsync();
        Message sent = conversation.AddTextMessage(sender, "Body", T1);
        await SaveAsync(conversation);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT sender FROM messages WHERE id = '{sent.Id.Value}'");

        Assert.Equal(sender.ToString(), stored as string);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(sender, Assert.Single(reloaded.Messages).Sender);
    }

    [Fact]
    public async Task A_system_message_round_trips_with_system_sender_and_content_type()
    {
        Conversation conversation = await NewConversationAsync();
        Message sent = conversation.AddSystemMessage("Handed to a human agent.", T1);
        await SaveAsync(conversation);

        object? storedContentType = await database.ExecuteScalarAsync(
            $"SELECT content_type FROM messages WHERE id = '{sent.Id.Value}'");

        Assert.Equal("System", storedContentType as string);

        Message message = Assert.Single((await LoadAsync(conversation.Id)).Messages);

        Assert.Equal(MessageSender.System, message.Sender);
        Assert.Equal(MessageContentType.System, message.ContentType);
        Assert.Equal("Handed to a human agent.", message.Text);
    }

    [Fact]
    public async Task Message_text_round_trips_including_interior_whitespace_and_non_latin_script()
    {
        const string Body = "Line one\nLine two\n\n\tIndented شكراً";

        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, Body, T1);
        await SaveAsync(conversation);

        Message message = Assert.Single((await LoadAsync(conversation.Id)).Messages);

        Assert.Equal(Body, message.Text);
    }

    [Fact]
    public async Task Multiple_messages_round_trip_with_distinct_identities()
    {
        Conversation conversation = await NewConversationAsync();
        Message first = conversation.AddTextMessage(MessageSender.Customer, "One", T1);
        Message second = conversation.AddTextMessage(MessageSender.AiAgent, "Two", T2);
        Message third = conversation.AddSystemMessage("Three", T3);

        await SaveAsync(conversation);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(3, reloaded.Messages.Count);
        Assert.Equal(
            [first.Id, second.Id, third.Id],
            reloaded.Messages.Select(message => message.Id));
    }

    // ------------------------------------------------------- ingestion ordering

    [Fact]
    public async Task Messages_reload_in_ingestion_order_not_in_timestamp_order()
    {
        // The accepted Phase 3 behaviour: a message can be ingested after one
        // that was sent later. Persistence must reproduce the order this platform
        // received them in, which is T1, T3, T2 -- not the sorted T1, T2, T3.
        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, "ingested 1st, sent T1", T1);
        conversation.AddTextMessage(MessageSender.Customer, "ingested 2nd, sent T3", T3);
        conversation.AddTextMessage(MessageSender.Customer, "ingested 3rd, sent T2", T2);

        await SaveAsync(conversation);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(
            ["ingested 1st, sent T1", "ingested 2nd, sent T3", "ingested 3rd, sent T2"],
            reloaded.Messages.Select(message => message.Text));

        // Stated as the instants themselves: T1, T3, T2 -- and emphatically not
        // T1, T2, T3.
        Assert.Equal([T1, T3, T2], reloaded.Messages.Select(message => message.SentAt));
        Assert.NotEqual([T1, T2, T3], reloaded.Messages.Select(message => message.SentAt));
    }

    [Fact]
    public async Task Out_of_order_timestamps_are_not_rewritten_by_persistence()
    {
        Conversation conversation = await NewConversationAsync();
        Message sentLater = conversation.AddTextMessage(MessageSender.Customer, "sent T3", T3);
        Message delayed = conversation.AddTextMessage(MessageSender.Customer, "sent T2", T2);

        await SaveAsync(conversation);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(T3, reloaded.Messages[0].SentAt);
        Assert.Equal(T2, reloaded.Messages[1].SentAt);
        Assert.Equal(sentLater.Id, reloaded.Messages[0].Id);
        Assert.Equal(delayed.Id, reloaded.Messages[1].Id);

        // The conversation's high-water mark stayed at the later instant.
        Assert.Equal(T3, reloaded.LastActivityAt);
    }

    [Fact]
    public async Task Ingestion_order_holds_when_messages_arrive_in_separate_units_of_work()
    {
        // The realistic case: three separate webhook deliveries, each its own
        // transaction, arriving in an order unrelated to when they were sent.
        Conversation conversation = await NewConversationAsync();
        await SaveAsync(conversation);

        await AppendInOwnTransactionAsync(conversation.Id, "delivery 1, sent T1", T1);
        await AppendInOwnTransactionAsync(conversation.Id, "delivery 2, sent T3", T3);
        await AppendInOwnTransactionAsync(conversation.Id, "delivery 3, sent T2", T2);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(
            ["delivery 1, sent T1", "delivery 2, sent T3", "delivery 3, sent T2"],
            reloaded.Messages.Select(message => message.Text));
        Assert.Equal([T1, T3, T2], reloaded.Messages.Select(message => message.SentAt));
    }

    [Fact]
    public async Task The_ingestion_sequence_follows_append_order_and_strictly_increases()
    {
        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, "a", T3);
        conversation.AddTextMessage(MessageSender.Customer, "b", T1);
        conversation.AddTextMessage(MessageSender.Customer, "c", T2);

        await SaveAsync(conversation);

        // The stored order is the aggregate's append order, regardless of the
        // sent instants.
        object? stored = await database.ExecuteScalarAsync(
            "SELECT string_agg(text, ',' ORDER BY ingestion_sequence) FROM messages " +
            $"WHERE conversation_id = '{conversation.Id.Value}'");

        Assert.Equal("a,b,c", stored as string);

        // The values come from a sequence, so they ascend but are not contiguous
        // from zero. Only the relation between them is meaningful.
        long[] sequences = await IngestionSequencesAsync(conversation.Id);

        Assert.Equal(3, sequences.Length);
        Assert.True(
            sequences[0] < sequences[1] && sequences[1] < sequences[2],
            $"Expected strictly increasing sequence values, got {string.Join(",", sequences)}.");
    }

    [Fact]
    public async Task The_ingestion_sequence_continues_across_separate_units_of_work()
    {
        Conversation conversation = await NewConversationAsync();
        await SaveAsync(conversation);

        await AppendInOwnTransactionAsync(conversation.Id, "first", T1);
        await AppendInOwnTransactionAsync(conversation.Id, "second", T3);
        await AppendInOwnTransactionAsync(conversation.Id, "third", T2);

        // Each append is its own transaction and none of them read the existing
        // messages, yet the values continue upwards rather than restarting --
        // because the database allocates them, not the loaded history.
        long[] sequences = await IngestionSequencesAsync(conversation.Id);

        Assert.Equal(3, sequences.Length);
        Assert.True(
            sequences[0] < sequences[1] && sequences[1] < sequences[2],
            $"Expected strictly increasing sequence values, got {string.Join(",", sequences)}.");
    }

    // ------------------------------------------- appending without the history

    [Fact]
    public async Task A_message_is_appended_without_loading_the_existing_history()
    {
        // The requirement this whole mechanism exists for: the write path must
        // not have to read a conversation's transcript in order to add to it.
        Conversation conversation = await NewConversationAsync();
        await SaveAsync(conversation);

        await AppendInOwnTransactionAsync(conversation.Id, "stored 1, sent T1", T1);
        await AppendInOwnTransactionAsync(conversation.Id, "stored 2, sent T3", T3);
        await AppendInOwnTransactionAsync(conversation.Id, "stored 3, sent T2", T2);

        await using (OmniAiDbContext writer = database.CreateContext())
        {
            // Only the conversation row. No Include, no message query.
            Conversation loaded = await writer.Conversations.SingleAsync(c => c.Id == conversation.Id);

            // Proof the history really is absent from this unit of work.
            Assert.Empty(loaded.Messages);
            Assert.Empty(writer.ChangeTracker.Entries<Message>());

            loaded.AddTextMessage(MessageSender.AiAgent, "appended blind", T3);

            await writer.SaveChangesAsync();
        }

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(
            ["stored 1, sent T1", "stored 2, sent T3", "stored 3, sent T2", "appended blind"],
            reloaded.Messages.Select(message => message.Text));
    }

    [Fact]
    public async Task Appending_to_a_large_history_never_queries_the_history()
    {
        // A structural assertion, not a benchmark. The interceptor records every
        // statement the append actually sends; if the mechanism needed the
        // history, a SELECT against messages would appear among them. How long
        // this takes is irrelevant -- that the statement is absent is the point.
        const int existing = 500;

        Conversation conversation = await NewConversationAsync();

        for (int index = 0; index < existing; index++)
        {
            conversation.AddTextMessage(
                MessageSender.Customer,
                $"m{index:D3}",
                T1.AddSeconds(index));
        }

        await SaveAsync(conversation);

        RecordedSql recorder = new();

        await using (OmniAiDbContext writer = database.CreateContext(recorder))
        {
            Conversation loaded = await writer.Conversations.SingleAsync(c => c.Id == conversation.Id);

            recorder.Clear();

            loaded.AddTextMessage(MessageSender.AiAgent, "appended to a long transcript", T3);
            await writer.SaveChangesAsync();

            // The aggregate held one message, never five hundred and one.
            Assert.Single(loaded.Messages);
            Assert.Single(writer.ChangeTracker.Entries<Message>());
        }

        Assert.DoesNotContain(
            recorder.Statements,
            statement => statement.Contains("FROM messages", StringComparison.OrdinalIgnoreCase));

        // And it did allocate from the sequence rather than deriving a value.
        Assert.Contains(
            recorder.Statements,
            statement => statement.Contains("nextval", StringComparison.OrdinalIgnoreCase));

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(existing + 1, reloaded.Messages.Count);
        Assert.Equal(
            [.. Enumerable.Range(0, existing).Select(index => $"m{index:D3}"), "appended to a long transcript"],
            reloaded.Messages.Select(message => message.Text));
    }

    // ----------------------------------------- the allocator's own guarantees

    [Fact]
    public async Task The_allocator_is_a_real_postgresql_sequence()
    {
        object? sequences = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_sequences WHERE sequencename = 'message_ingestion_sequence'");

        Assert.Equal(1L, sequences as long?);

        // Two allocations from one sequence can never collide, which is what
        // makes concurrent writers safe without taking a lock.
        object? distinct = await database.ExecuteScalarAsync(
            "SELECT count(DISTINCT v) FROM (" +
            "SELECT nextval('message_ingestion_sequence') AS v UNION ALL " +
            "SELECT nextval('message_ingestion_sequence') AS v) allocations");

        Assert.Equal(2L, distinct as long?);
    }

    [Fact]
    public async Task No_two_messages_ever_share_an_ingestion_sequence_value()
    {
        Conversation first = await NewConversationAsync();
        first.AddTextMessage(MessageSender.Customer, "one", T1);
        first.AddTextMessage(MessageSender.Customer, "two", T2);
        await SaveAsync(first);

        Conversation second = await NewConversationAsync();
        second.AddTextMessage(MessageSender.Customer, "three", T1);
        await SaveAsync(second);

        await AppendInOwnTransactionAsync(first.Id, "four", T3);

        object? collisions = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM (SELECT ingestion_sequence FROM messages " +
            "GROUP BY ingestion_sequence HAVING count(*) > 1) duplicates");

        Assert.Equal(0L, collisions as long?);
    }

    [Fact]
    public async Task A_duplicate_ingestion_position_is_rejected_by_the_database()
    {
        // The safeguard behind the ordering value. The sequence cannot hand out a
        // duplicate, so this does not test the allocator; it proves the database
        // itself refuses one, which is what protects the transcript from any
        // future code path that sets the column directly.
        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, "already stored", T1);
        await SaveAsync(conversation);

        Npgsql.PostgresException failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => database.ExecuteScalarAsync(
                "INSERT INTO messages (id, conversation_id, sender, content_type, text, sent_at, ingestion_sequence) " +
                $"SELECT '{Guid.CreateVersion7()}', '{conversation.Id.Value}', 'Customer', 'Text', " +
                "'clashing position', now(), ingestion_sequence FROM messages " +
                $"WHERE conversation_id = '{conversation.Id.Value}' LIMIT 1"));

        Assert.Contains("ix_messages_conversation_ingestion", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gap_left_by_a_rolled_back_append_does_not_disturb_later_order()
    {
        // Sequences do not roll back, so a failed transaction burns the value it
        // took. That is the accepted cost of not locking: the gaps mean nothing,
        // the ordering still means everything.
        Conversation conversation = await NewConversationAsync();
        await SaveAsync(conversation);

        await AppendInOwnTransactionAsync(conversation.Id, "first", T1);

        await using (OmniAiDbContext abandoned = database.CreateContext())
        {
            await using IDbContextTransaction transaction =
                await abandoned.Database.BeginTransactionAsync();

            Conversation loaded = await abandoned.Conversations.SingleAsync(c => c.Id == conversation.Id);
            loaded.AddTextMessage(MessageSender.Customer, "never committed", T2);
            await abandoned.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        await AppendInOwnTransactionAsync(conversation.Id, "second", T3);

        Conversation reloaded = await LoadAsync(conversation.Id);

        Assert.Equal(["first", "second"], reloaded.Messages.Select(message => message.Text));

        long[] sequences = await IngestionSequencesAsync(conversation.Id);

        Assert.Equal(2, sequences.Length);
        Assert.True(
            sequences[1] - sequences[0] > 1,
            $"Expected a gap where the rolled back append consumed a value, got {string.Join(",", sequences)}.");
    }

    [Fact]
    public async Task Message_identity_is_still_the_primary_key()
    {
        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, "keyed", T1);
        await SaveAsync(conversation);

        object? primaryKeyColumn = await database.ExecuteScalarAsync(
            "SELECT a.attname FROM pg_index i " +
            "JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey) " +
            "WHERE i.indrelid = 'messages'::regclass AND i.indisprimary");

        Assert.Equal("id", primaryKeyColumn as string);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_messages()
    {
        // Cascade here, unlike the tenant relationship: messages are inside the
        // conversation aggregate and have no meaning without it.
        Conversation conversation = await NewConversationAsync();
        conversation.AddTextMessage(MessageSender.Customer, "doomed", T1);
        await SaveAsync(conversation);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Conversation loaded = await context.Conversations.SingleAsync(c => c.Id == conversation.Id);
            context.Conversations.Remove(loaded);
            await context.SaveChangesAsync();
        }

        object? remaining = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM messages WHERE conversation_id = '{conversation.Id.Value}'");

        Assert.Equal(0L, remaining as long?);
    }

    private async Task<Conversation> NewConversationAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return Conversation.Start(tenant.Id, ChannelKind.WhatsApp, T1);
    }

    private async Task SaveAsync(Conversation conversation)
    {
        await using OmniAiDbContext context = database.CreateContext();

        if (await context.Conversations.AnyAsync(c => c.Id == conversation.Id))
        {
            return;
        }

        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The production write path: load the conversation row, append, save. It
    /// deliberately does not load the message history, so every test that goes
    /// through it also demonstrates that appending does not need one.
    /// </summary>
    private async Task AppendInOwnTransactionAsync(ConversationId id, string text, DateTimeOffset sentAt)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Conversation loaded = await context.Conversations.SingleOrDefaultAsync(c => c.Id == id)
            ?? throw new InvalidOperationException("Conversation was not persisted.");

        Assert.Empty(loaded.Messages);

        loaded.AddTextMessage(MessageSender.Customer, text, sentAt);
        await context.SaveChangesAsync();
    }

    private async Task<long[]> IngestionSequencesAsync(ConversationId id)
    {
        object? aggregated = await database.ExecuteScalarAsync(
            "SELECT string_agg(ingestion_sequence::text, ',' ORDER BY ingestion_sequence) " +
            $"FROM messages WHERE conversation_id = '{id.Value}'");

        return aggregated is string text && text.Length > 0
            ? [.. text.Split(',').Select(value => long.Parse(value, CultureInfo.InvariantCulture))]
            : [];
    }

    private async Task<Conversation> LoadAsync(ConversationId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.FindConversationWithMessagesAsync(id)
            ?? throw new InvalidOperationException("Conversation was not persisted.");
    }
}
