using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Customers;
using OmniAI.Application.Customers.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Customers;

/// <summary>
/// Combining two customers, against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here can be proved against a fake.</strong> The locking, the
/// set-based moves, the composite foreign keys and the cycle rule are all
/// database behaviour, and the failures worth catching — a deadlock, a cycle, a
/// half-moved history — are the ones a single-threaded in-memory double is least
/// able to produce.
/// </para>
/// <para>
/// Every name and number in this file is synthetic.
/// </para>
/// </remarks>
public sealed class CustomerMergeTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------- the happy path

    [Fact]
    public async Task A_same_workspace_merge_moves_identities_and_history()
    {
        World world = await World.CreateAsync(database);

        Customer source = await world.CustomerAsync("0000111122223333");
        Customer target = await world.CustomerAsync("0000444455556666");

        await world.ConversationAsync(source, ChannelKind.WhatsApp);
        await world.ConversationAsync(source, ChannelKind.WebChat);
        await world.ConversationAsync(target, ChannelKind.WhatsApp);

        (CustomerMergeCounts counts, bool already) = await world.MergeAsync(source, target);

        Assert.False(already);
        Assert.Equal(2, counts.Conversations);
        Assert.Equal(1, counts.Identities);

        await using OmniAiDbContext context = database.CreateContext();

        // Every conversation now belongs to the survivor.
        Assert.Equal(
            3,
            await context.Conversations.CountAsync(c => c.CustomerId == target.Id));

        Assert.Equal(
            0,
            await context.Conversations.CountAsync(c => c.CustomerId == source.Id));

        // So does every channel identity.
        Assert.Equal(
            2,
            await context.WhatsAppCustomerIdentities.CountAsync(i => i.CustomerId == target.Id));

        // And the source is a tombstone pointing at it, not a deletion.
        Customer tombstone = await context.Customers.SingleAsync(c => c.Id == source.Id);

        Assert.Equal(target.Id, tombstone.MergedIntoCustomerId);
        Assert.Equal(Noon, tombstone.MergedAt);
        Assert.NotNull(tombstone.MergedBy);
    }

    [Fact]
    public async Task A_merge_leaves_every_message_exactly_as_it_was()
    {
        // The claim that matters most: combining two customer records is an
        // identity change and must not touch a single thing a customer said, nor
        // the order it arrived in.
        World world = await World.CreateAsync(database);

        Customer source = await world.CustomerAsync("0000111122223333");
        Customer target = await world.CustomerAsync("0000444455556666");

        ConversationId conversationId = await world.ConversationAsync(
            source,
            ChannelKind.WhatsApp,
            "First",
            "Second",
            "Third");

        (string[] TextsBefore, long[] SequencesBefore) before = await ReadAsync(conversationId);

        await world.MergeAsync(source, target);

        (string[] texts, long[] sequences) = await ReadAsync(conversationId);

        Assert.Equal(before.TextsBefore, texts);
        Assert.Equal(before.SequencesBefore, sequences);
    }

    // ------------------------------------------------------------- refusals

    [Fact]
    public async Task A_cross_workspace_merge_is_refused()
    {
        World first = await World.CreateAsync(database);
        World second = await World.CreateAsync(database);

        Customer source = await first.CustomerAsync("0000111122223333");
        Customer target = await second.CustomerAsync("0000444455556666");

        // Asked in the source's workspace, which is the only workspace the caller
        // could be acting in. The target is simply not there.
        await Assert.ThrowsAsync<CustomerMergeNotAllowedException>(
            () => first.MergeAsync(source, target));

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Null((await context.Customers.SingleAsync(c => c.Id == source.Id))
            .MergedIntoCustomerId);
    }

    [Fact]
    public async Task Merging_a_customer_into_itself_is_refused()
    {
        World world = await World.CreateAsync(database);

        Customer customer = await world.CustomerAsync("0000111122223333");

        await Assert.ThrowsAsync<CustomerMergeNotAllowedException>(
            () => world.MergeAsync(customer, customer));
    }

    [Fact]
    public async Task Merging_into_an_already_merged_customer_is_refused_with_its_canonical_id()
    {
        World world = await World.CreateAsync(database);

        Customer a = await world.CustomerAsync("0000111122223333");
        Customer b = await world.CustomerAsync("0000444455556666");
        Customer c = await world.CustomerAsync("0000777788889999");

        await world.MergeAsync(a, b);

        // c -> a is refused: a is a tombstone, and allowing it would build a
        // chain.
        CustomerAlreadyMergedException failure =
            await Assert.ThrowsAsync<CustomerAlreadyMergedException>(
                () => world.MergeAsync(c, a));

        Assert.Equal(a.Id, failure.CustomerId);
        Assert.Equal(b.Id, failure.CanonicalId);
    }

    [Fact]
    public async Task A_source_already_merged_elsewhere_is_not_silently_remapped()
    {
        World world = await World.CreateAsync(database);

        Customer source = await world.CustomerAsync("0000111122223333");
        Customer first = await world.CustomerAsync("0000444455556666");
        Customer second = await world.CustomerAsync("0000777788889999");

        await world.MergeAsync(source, first);

        CustomerAlreadyMergedException failure =
            await Assert.ThrowsAsync<CustomerAlreadyMergedException>(
                () => world.MergeAsync(source, second));

        Assert.Equal(first.Id, failure.CanonicalId);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(
            first.Id,
            (await context.Customers.SingleAsync(c => c.Id == source.Id)).MergedIntoCustomerId);
    }

    // ---------------------------------------------------------- idempotency

    [Fact]
    public async Task Repeating_the_same_merge_is_deterministic_and_moves_nothing_twice()
    {
        World world = await World.CreateAsync(database);

        Customer source = await world.CustomerAsync("0000111122223333");
        Customer target = await world.CustomerAsync("0000444455556666");

        await world.ConversationAsync(source, ChannelKind.WhatsApp);

        (CustomerMergeCounts first, bool alreadyFirst) = await world.MergeAsync(source, target);

        (CustomerMergeCounts second, bool alreadySecond) = await world.MergeAsync(source, target);

        Assert.False(alreadyFirst);
        Assert.Equal(1, first.Conversations);

        // A retried request, a double-click and a stale tab all produce this.
        // Answering "done" is the only response that does not invite the caller to
        // try something else.
        Assert.True(alreadySecond);
        Assert.Equal(0, second.Conversations);
        Assert.Equal(0, second.Identities);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(1, await context.Conversations.CountAsync(c => c.CustomerId == target.Id));
    }

    // --------------------------------------------------------- concurrency

    [Fact]
    public async Task Two_merges_of_the_same_source_leave_exactly_one_winner()
    {
        World world = await World.CreateAsync(database);

        Customer source = await world.CustomerAsync("0000111122223333");
        Customer first = await world.CustomerAsync("0000444455556666");
        Customer second = await world.CustomerAsync("0000777788889999");

        await world.ConversationAsync(source, ChannelKind.WhatsApp);

        ConcurrencyToken version = await world.VersionAsync(source);

        Task<bool> left = AttemptAsync(world, source, first, version);
        Task<bool> right = AttemptAsync(world, source, second, version);

        bool[] outcomes = await Task.WhenAll(left, right);

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));

        await using OmniAiDbContext context = database.CreateContext();

        Customer tombstone = await context.Customers.SingleAsync(c => c.Id == source.Id);

        // Whichever won, the source points at exactly one of them and its
        // conversation followed it there.
        Assert.NotNull(tombstone.MergedIntoCustomerId);

        Assert.Equal(
            1,
            await context.Conversations.CountAsync(
                c => c.CustomerId == tombstone.MergedIntoCustomerId!.Value));
    }

    [Fact]
    public async Task Opposite_merges_of_one_pair_cannot_deadlock_or_form_a_cycle()
    {
        // A -> B and B -> A, at the same moment. The locks are taken in an order
        // derived from the ids rather than from which record the caller called the
        // source, so the two queue behind each other instead of each holding what
        // the other waits for.
        World world = await World.CreateAsync(database);

        Customer a = await world.CustomerAsync("0000111122223333");
        Customer b = await world.CustomerAsync("0000444455556666");

        ConcurrencyToken versionA = await world.VersionAsync(a);
        ConcurrencyToken versionB = await world.VersionAsync(b);

        bool[] outcomes = await Task.WhenAll(
            AttemptAsync(world, a, b, versionA),
            AttemptAsync(world, b, a, versionB));

        // Exactly one direction can survive: once A points at B, B has a merged
        // target and is refused.
        Assert.Equal(1, outcomes.Count(succeeded => succeeded));

        await using OmniAiDbContext context = database.CreateContext();

        Customer reloadedA = await context.Customers.SingleAsync(c => c.Id == a.Id);
        Customer reloadedB = await context.Customers.SingleAsync(c => c.Id == b.Id);

        // No cycle: at most one of the pair points anywhere, and never at each
        // other.
        Assert.False(reloadedA.IsMerged && reloadedB.IsMerged);
    }

    // --------------------------------------------------------------- helpers

    /// <summary>Runs one merge, reporting whether it succeeded rather than throwing.</summary>
    private static async Task<bool> AttemptAsync(
        World world,
        Customer source,
        Customer target,
        ConcurrencyToken version)
    {
        try
        {
            await world.MergeAsync(source, target, version);

            return true;
        }
        catch (CustomerAlreadyMergedException)
        {
            return false;
        }
        catch (CustomerVersionMismatchException)
        {
            return false;
        }
        catch (CustomerMergeNotAllowedException)
        {
            return false;
        }
    }

    private async Task<(string[] Texts, long[] Sequences)> ReadAsync(ConversationId conversationId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Conversation conversation = await context.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .SingleAsync(c => c.Id == conversationId);

        return (
            [.. conversation.Messages.Select(message => message.Text)],
            [.. conversation.Messages.Select(message => message.SentAt.ToUnixTimeMilliseconds())]);
    }

    /// <summary>One workspace, with the pieces a merge acts on.</summary>
    private sealed class World
    {
        private readonly PostgresTestDatabase _database;

        private World(PostgresTestDatabase database, TenantId tenantId)
        {
            _database = database;
            TenantId = tenantId;
        }

        public TenantId TenantId { get; }

        public static async Task<World> CreateAsync(PostgresTestDatabase database)
        {
            Tenant tenant = Tenant.Create(
                TenantName.Create("Acme"),
                TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

            await using OmniAiDbContext context = database.CreateContext();

            context.Tenants.Add(tenant);

            await context.SaveChangesAsync();

            return new World(database, tenant.Id);
        }

        /// <summary>A customer with one WhatsApp identity.</summary>
        public async Task<Customer> CustomerAsync(string number)
        {
            Customer customer = Customer.Create(TenantId, displayName: null, Noon);

            await using OmniAiDbContext context = _database.CreateContext();

            context.Customers.Add(customer);

            context.WhatsAppCustomerIdentities.Add(WhatsAppCustomerIdentity.Create(
                TenantId,
                WhatsAppCustomerId.Create(number),
                customer.Id,
                Noon));

            await context.SaveChangesAsync();

            return customer;
        }

        public async Task<ConversationId> ConversationAsync(
            Customer customer,
            ChannelKind channel,
            params string[] texts)
        {
            Conversation conversation = Conversation.Start(TenantId, customer.Id, channel, Noon);

            for (int i = 0; i < texts.Length; i++)
            {
                conversation.AddTextMessage(
                    MessageSender.Customer,
                    texts[i],
                    Noon.AddMinutes(i + 1));
            }

            await using OmniAiDbContext context = _database.CreateContext();

            context.Conversations.Add(conversation);

            await context.SaveChangesAsync();

            return conversation.Id;
        }

        public async Task<ConcurrencyToken> VersionAsync(Customer customer)
        {
            await using OmniAiDbContext context = _database.CreateContext();

            CustomerRepository repository = new(context);

            Versioned<Customer>? found = await repository.FindAsync(TenantId, customer.Id);

            return found!.Version;
        }

        public async Task<(CustomerMergeCounts Counts, bool AlreadyMerged)> MergeAsync(
            Customer source,
            Customer target,
            ConcurrencyToken? version = null)
        {
            ConcurrencyToken expected = version ?? await VersionAsync(source);

            await using OmniAiDbContext context = _database.CreateContext();

            CustomerRepository repository = new(context);

            return await repository.MergeAsync(
                TenantId,
                source.Id,
                target.Id,
                UserId.New(),
                expected,
                Noon);
        }
    }
}
