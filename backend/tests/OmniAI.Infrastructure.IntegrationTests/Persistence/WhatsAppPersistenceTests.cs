using OmniAI.Domain.Customers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Security;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Security;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What PostgreSQL itself enforces about the WhatsApp channel.
/// </summary>
/// <remarks>
/// <strong>These are the rules that survive a bug in a handler.</strong> Every
/// property here is also checked in application code; asserted against the real
/// database, they keep holding when somebody refactors the code that checks them.
/// <para>
/// Every identifier and every token in this suite is invented. There is no Meta
/// account and no network access anywhere in it.
/// </para>
/// </remarks>
public sealed class WhatsAppPersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>, IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string Token = "fake-local-test-token-not-a-real-credential";

    /// <summary>
    /// Empties the channel's tables before each test.
    /// </summary>
    /// <remarks>
    /// Several assertions here are about how much exists -- one inbound record,
    /// one current contact mapping, nothing left claimable. Those are the
    /// properties worth asserting, and they are only meaningful when the test
    /// owns everything in the table. The suite shares one database, so each test
    /// starts by clearing what the last one left.
    /// </remarks>
    public async Task InitializeAsync() =>
        await database.ExecuteScalarAsync(
            "TRUNCATE whatsapp_deliveries, whatsapp_inbound_messages, whatsapp_contacts, "
            + "whatsapp_credentials, whatsapp_connections CASCADE");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_phone_number_belongs_to_one_connection_across_the_whole_deployment()
    {
        Tenant first = await NewTenantAsync();
        Tenant second = await NewTenantAsync();

        string number = Number();

        await ConnectAsync(first, number);

        // <strong>Deployment-wide, not per workspace.</strong> Routing an inbound
        // message uses the phone number id and nothing else, so a second claim on
        // one number would mean one company's customers reaching another
        // company's inbox.
        await Assert.ThrowsAsync<WhatsAppPhoneNumberInUseException>(
            () => ConnectAsync(second, number));
    }

    [Fact]
    public async Task The_stored_credential_is_ciphertext_and_never_the_token()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        AesGcmSecretProtector protector = new(RandomNumberGenerator.GetBytes(32));

        SecretContext context = new(
            SecretPurposes.WhatsAppAccessToken,
            tenant.Id.Value,
            connection.Id.Value);

        await using OmniAiDbContext writeContext = database.CreateContext();

        WhatsAppConnectionRepository repository = new(writeContext);

        Versioned<WhatsAppConnection> stored = (await repository
            .FindAsync(tenant.Id, connection.Id))!;

        stored.Value.RecordCredentialStored(Noon);

        await repository.ReplaceCredentialAsync(
            stored.Value,
            protector.Protect(Token, context),
            stored.Version);

        // Read as raw bytes through SQL, not through the repository, because the
        // question is what a backup contains.
        object? asText = await database.ExecuteScalarAsync(
            "SELECT encode(protected_token, 'escape') FROM whatsapp_credentials");

        Assert.DoesNotContain(Token, asText as string ?? string.Empty, StringComparison.Ordinal);

        object? hex = await database.ExecuteScalarAsync(
            "SELECT encode(protected_token, 'hex') FROM whatsapp_credentials");

        Assert.DoesNotContain(
            Convert.ToHexString(Encoding.UTF8.GetBytes(Token)),
            hex as string ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        // And it is still usable by the deployment that wrote it.
        await using OmniAiDbContext readContext = database.CreateContext();

        byte[]? sealedToken = await new WhatsAppConnectionRepository(readContext)
            .FindProtectedCredentialAsync(connection.Id);

        Assert.Equal(Token, protector.Unprotect(sealedToken, context));
    }

    [Fact]
    public async Task No_connection_read_path_returns_a_credential()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppConnectionRepository repository = new(context);

        // The list row and the aggregate are the only two shapes an API can
        // build a response from, and neither has anywhere to put a token.
        IReadOnlyList<WhatsAppConnectionRow> rows = await repository.ListAsync(tenant.Id);

        Assert.DoesNotContain(
            typeof(WhatsAppConnectionRow).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));

        Assert.Single(rows);

        Versioned<WhatsAppConnection>? found = await repository.FindAsync(tenant.Id, connection.Id);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task A_connection_from_another_workspace_is_simply_not_found()
    {
        Tenant owner = await NewTenantAsync();
        Tenant outsider = await NewTenantAsync();

        WhatsAppConnection connection = await ConnectAsync(owner, Number());

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Null(await new WhatsAppConnectionRepository(context)
            .FindAsync(outsider.Id, connection.Id));
    }

    [Fact]
    public async Task Routing_answers_only_for_a_connection_that_is_serving()
    {
        Tenant tenant = await NewTenantAsync();
        string number = Number();

        WhatsAppConnection connection = await ConnectAsync(tenant, number);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            WhatsAppRoute? draft = await new WhatsAppConnectionRepository(context)
                .FindRouteAsync(PhoneNumberId.Create(number));

            Assert.NotNull(draft);
            Assert.False(draft.IsServing);
        }

        await ActivateAsync(connection);

        await using OmniAiDbContext reread = database.CreateContext();

        WhatsAppRoute? live = await new WhatsAppConnectionRepository(reread)
            .FindRouteAsync(PhoneNumberId.Create(number));

        Assert.True(live!.IsServing);
        Assert.Equal(tenant.Id, live.TenantId);
    }

    [Fact]
    public async Task The_same_provider_message_id_can_be_recorded_only_once()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        ProviderMessageId id = ProviderMessageId.Create("wamid.ONCE==");

        Assert.True(await RecordAsync(connection, tenant, id, "Hello"));

        // The index is the authority, not a check in a handler: two redeliveries
        // racing each other both pass any in-process test.
        Assert.False(await RecordAsync(connection, tenant, id, "Hello"));

        Assert.Equal(1L, await CountAsync("whatsapp_inbound_messages"));
    }

    [Fact]
    public async Task Two_workers_claiming_at_once_never_receive_the_same_message()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        for (int index = 0; index < 20; index++)
        {
            await RecordAsync(
                connection,
                tenant,
                ProviderMessageId.Create($"wamid.BATCH{index}=="),
                $"Message {index}");
        }

        // Separate contexts, so these are separate connections doing genuinely
        // concurrent work -- which is the only arrangement that can fail.
        await using OmniAiDbContext firstWorker = database.CreateContext();
        await using OmniAiDbContext secondWorker = database.CreateContext();

        Task<IReadOnlyList<ClaimedInboundMessage>> first =
            new WhatsAppInbox(firstWorker).ClaimAsync(20, 5, Noon);

        Task<IReadOnlyList<ClaimedInboundMessage>> second =
            new WhatsAppInbox(secondWorker).ClaimAsync(20, 5, Noon);

        IReadOnlyList<ClaimedInboundMessage>[] batches = await Task.WhenAll(first, second);

        List<Guid> claimed =
        [
            .. batches.SelectMany(batch => batch.Select(message => message.Id.Value)),
        ];

        Assert.Equal(20, claimed.Count);
        Assert.Equal(claimed.Count, claimed.Distinct().Count());
    }

    [Fact]
    public async Task A_claim_left_by_a_dead_worker_is_returned_to_the_queue()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        await RecordAsync(
            connection,
            tenant,
            ProviderMessageId.Create("wamid.CRASH=="),
            "Anyone there?");

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppInbox inbox = new(context);

        // Claimed, and then the process disappears: nothing reports, nothing
        // releases, the row simply stops moving.
        Assert.Single(await inbox.ClaimAsync(10, 5, Noon));
        Assert.Empty(await inbox.ClaimAsync(10, 5, Noon));

        int released = await inbox.ReleaseStaleClaimsAsync(Noon.AddMinutes(5));

        Assert.Equal(1, released);

        // Back in the queue, with the attempt still counted -- so a message that
        // kills its worker every time runs out rather than looping for ever.
        ClaimedInboundMessage again = Assert.Single(await inbox.ClaimAsync(10, 5, Noon));

        Assert.Equal(2, again.Attempts);
    }

    [Fact]
    public async Task A_message_that_has_used_its_attempts_is_not_claimed_again()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());

        await RecordAsync(connection, tenant, ProviderMessageId.Create("wamid.LOOP=="), "Hi");

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppInbox inbox = new(context);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            ClaimedInboundMessage claimed = Assert.Single(await inbox.ClaimAsync(10, 3, Noon));

            await inbox.ReleaseAsync(claimed.Id, Noon);
        }

        Assert.Empty(await inbox.ClaimAsync(10, 3, Noon));
    }

    [Fact]
    public async Task One_internal_message_can_have_only_one_delivery()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());
        Conversation conversation = await NewConversationAsync(tenant);
        MessageId message = await AppendMessageAsync(conversation);

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppOutbox outbox = new(context);

        Assert.True(await outbox.QueueAsync(Delivery(connection, tenant, conversation, message)));

        // The customer reading the same reply twice is the failure this prevents,
        // and no amount of care in the queueing code rules it out under
        // concurrency.
        Assert.False(await outbox.QueueAsync(Delivery(connection, tenant, conversation, message)));

        Assert.Equal(1L, await CountAsync("whatsapp_deliveries"));
    }

    [Fact]
    public async Task Two_dispatchers_claiming_at_once_never_send_the_same_message_twice()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());
        Conversation conversation = await NewConversationAsync(tenant);

        await using (OmniAiDbContext seed = database.CreateContext())
        {
            WhatsAppOutbox outbox = new(seed);

            for (int index = 0; index < 20; index++)
            {
                MessageId message = await AppendMessageAsync(conversation);

                await outbox.QueueAsync(Delivery(connection, tenant, conversation, message));
            }
        }

        await using OmniAiDbContext firstWorker = database.CreateContext();
        await using OmniAiDbContext secondWorker = database.CreateContext();

        IReadOnlyList<ClaimedDelivery>[] batches = await Task.WhenAll(
            new WhatsAppOutbox(firstWorker).ClaimAsync(20, 1, Noon),
            new WhatsAppOutbox(secondWorker).ClaimAsync(20, 1, Noon));

        List<Guid> claimed =
        [
            .. batches.SelectMany(batch => batch.Select(delivery => delivery.Id.Value)),
        ];

        Assert.Equal(20, claimed.Count);
        Assert.Equal(claimed.Count, claimed.Distinct().Count());
    }

    [Fact]
    public async Task A_send_a_dead_worker_left_behind_becomes_unknown_and_is_never_resent()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());
        Conversation conversation = await NewConversationAsync(tenant);
        MessageId message = await AppendMessageAsync(conversation);

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppOutbox outbox = new(context);

        await outbox.QueueAsync(Delivery(connection, tenant, conversation, message));

        Assert.Single(await outbox.ClaimAsync(10, 1, Noon));

        int abandoned = await outbox.AbandonStaleSendsAsync(Noon.AddMinutes(10));

        Assert.Equal(1, abandoned);

        // Unknown, not re-queued. The worker may have died after Meta accepted
        // the message, and sending again would show the customer two copies.
        IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus> statuses =
            await outbox.ListStatusesAsync(conversation.Id);

        Assert.Equal(WhatsAppDeliveryStatus.Unknown, statuses[message.Value]);
        Assert.Empty(await outbox.ClaimAsync(10, 1, Noon));
    }

    [Fact]
    public async Task Delivery_statuses_are_idempotent_and_never_go_backwards()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ConnectAsync(tenant, Number());
        Conversation conversation = await NewConversationAsync(tenant);
        MessageId message = await AppendMessageAsync(conversation);

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppOutbox outbox = new(context);

        await outbox.QueueAsync(Delivery(connection, tenant, conversation, message));

        ClaimedDelivery claimed = Assert.Single(await outbox.ClaimAsync(10, 1, Noon));

        ProviderMessageId providerMessageId = ProviderMessageId.Create("wamid.STATUS==");

        await outbox.MarkSentAsync(claimed.Id, providerMessageId, Noon);

        Assert.True((await Apply(outbox, providerMessageId, WhatsAppDeliveryStatus.Read)).Changed);

        // Late "delivered", and a repeated "read". Both are ordinary provider
        // behaviour and neither may move what a colleague is looking at.
        Assert.False(
            (await Apply(outbox, providerMessageId, WhatsAppDeliveryStatus.Delivered)).Changed);

        Assert.False((await Apply(outbox, providerMessageId, WhatsAppDeliveryStatus.Read)).Changed);

        IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus> statuses =
            await outbox.ListStatusesAsync(conversation.Id);

        Assert.Equal(WhatsAppDeliveryStatus.Read, statuses[message.Value]);
    }

    [Fact]
    public async Task A_status_for_an_unknown_provider_message_matches_nothing()
    {
        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppStatusOutcome outcome = await new WhatsAppOutbox(context)
            .ApplyProviderStatusAsync(
                ProviderMessageId.Create("wamid.NOBODY=="),
                WhatsAppDeliveryStatus.Delivered,
                null,
                Noon);

        Assert.False(outcome.Matched);
        Assert.False(outcome.Changed);
    }

    [Fact]
    public async Task A_customer_has_one_current_conversation_per_connection()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ActivateAsync(await ConnectAsync(tenant, Number()));

        WhatsAppCustomerId customer = WhatsAppCustomerId.Create("447700900123");

        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppContactStore contacts = new(context);

        WhatsAppRoute route = (await new WhatsAppConnectionRepository(context)
            .FindRouteAsync(connection.PhoneNumberId))!;

        ConversationId first = await contacts.ResolveConversationAsync(route, customer, Noon);
        ConversationId again = await contacts.ResolveConversationAsync(route, customer, Noon);

        // A person messaging a business is one continuing conversation. A second
        // one would split their history in half without either side noticing.
        Assert.Equal(first, again);
        Assert.Equal(1L, await CountAsync("whatsapp_contacts"));
    }

    [Fact]
    public async Task A_closed_conversation_is_superseded_by_a_new_open_one()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ActivateAsync(await ConnectAsync(tenant, Number()));

        WhatsAppCustomerId customer = WhatsAppCustomerId.Create("447700900456");

        ConversationId first;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            WhatsAppRoute route = (await new WhatsAppConnectionRepository(context)
                .FindRouteAsync(connection.PhoneNumberId))!;

            first = await new WhatsAppContactStore(context)
                .ResolveConversationAsync(route, customer, Noon);
        }

        await using (OmniAiDbContext closing = database.CreateContext())
        {
            Conversation conversation = await closing.Conversations.SingleAsync(
                candidate => candidate.Id == first);

            conversation.Close(Noon);

            await closing.SaveChangesAsync();
        }

        await using OmniAiDbContext reopening = database.CreateContext();

        WhatsAppRoute reread = (await new WhatsAppConnectionRepository(reopening)
            .FindRouteAsync(connection.PhoneNumberId))!;

        ConversationId second = await new WhatsAppContactStore(reopening)
            .ResolveConversationAsync(reread, customer, Noon.AddHours(1));

        Assert.NotEqual(first, second);

        // Exactly one mapping is current, which is the partial unique index doing
        // its job -- and both mappings survive, so the earlier conversation is
        // still reachable from the inbox.
        Assert.Equal(2L, await CountAsync("whatsapp_contacts"));
        Assert.Equal(
            1L,
            await CountAsync("whatsapp_contacts", "WHERE is_current"));
    }

    [Fact]
    public async Task Two_messages_from_one_customer_at_once_create_one_conversation()
    {
        Tenant tenant = await NewTenantAsync();
        WhatsAppConnection connection = await ActivateAsync(await ConnectAsync(tenant, Number()));

        WhatsAppCustomerId customer = WhatsAppCustomerId.Create("447700900789");

        await using OmniAiDbContext firstWorker = database.CreateContext();
        await using OmniAiDbContext secondWorker = database.CreateContext();

        WhatsAppRoute route = (await new WhatsAppConnectionRepository(firstWorker)
            .FindRouteAsync(connection.PhoneNumberId))!;

        // <strong>The race this phase's contact mapping exists to lose safely.</strong>
        // Two messages a second apart arrive as two webhooks and can be processed
        // by two workers at the same moment. Without the partial unique index both
        // would start a conversation, and the customer would be answered twice by
        // two threads that cannot see each other.
        ConversationId[] resolved = await Task.WhenAll(
            new WhatsAppContactStore(firstWorker).ResolveConversationAsync(route, customer, Noon),
            new WhatsAppContactStore(secondWorker).ResolveConversationAsync(route, customer, Noon));

        Assert.Equal(resolved[0], resolved[1]);
        Assert.Equal(1L, await CountAsync("whatsapp_contacts", "WHERE is_current"));
    }

    [Fact]
    public async Task A_conversation_that_did_not_come_from_whatsapp_has_no_link()
    {
        Tenant tenant = await NewTenantAsync();
        Conversation conversation = await NewConversationAsync(tenant);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Null(await new WhatsAppContactStore(context).FindLinkAsync(conversation.Id));
    }

    private static Task<WhatsAppStatusOutcome> Apply(
        WhatsAppOutbox outbox,
        ProviderMessageId providerMessageId,
        WhatsAppDeliveryStatus status) =>
        outbox.ApplyProviderStatusAsync(providerMessageId, status, null, Noon);

    private static WhatsAppDelivery Delivery(
        WhatsAppConnection connection,
        Tenant tenant,
        Conversation conversation,
        MessageId message) =>
        WhatsAppDelivery.Queue(
            connection.Id,
            tenant.Id,
            conversation.Id,
            message,
            WhatsAppCustomerId.Create("447700900123"),
            "Thanks for getting in touch.",
            Noon);

    /// <summary>A phone number id no other test in this run will claim.</summary>
    private static string Number() =>
        Random.Shared.NextInt64(100_000_000_000_000, 999_999_999_999_999)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<bool> RecordAsync(
        WhatsAppConnection connection,
        Tenant tenant,
        ProviderMessageId providerMessageId,
        string text)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new WhatsAppInbox(context).RecordAsync(InboundWhatsAppMessage.ForText(
            connection.Id,
            tenant.Id,
            providerMessageId,
            WhatsAppCustomerId.Create("447700900123"),
            text,
            Noon,
            Noon));
    }

    private async Task<long> CountAsync(string table, string? predicate = null) =>
        (await database.ExecuteScalarAsync($"SELECT count(*) FROM {table} {predicate}")) as long?
        ?? 0L;

    private async Task<Tenant> NewTenantAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant;
    }

    private async Task<WhatsAppConnection> ConnectAsync(Tenant tenant, string phoneNumberId)
    {
        AiAgent agent = AiAgent.Create(
            tenant.Id,
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Be helpful and concise."),
            Noon);

        agent.Activate(Noon);

        await using OmniAiDbContext seed = database.CreateContext();

        seed.AiAgents.Add(agent);

        await seed.SaveChangesAsync();

        WhatsAppConnection connection = WhatsAppConnection.Create(
            tenant.Id,
            agent.Id,
            WabaId.Create("111222333444555"),
            PhoneNumberId.Create(phoneNumberId),
            "+44 7700 900000",
            Noon);

        await using OmniAiDbContext context = database.CreateContext();

        await new WhatsAppConnectionRepository(context).AddAsync(connection);

        return connection;
    }

    private async Task<WhatsAppConnection> ActivateAsync(WhatsAppConnection connection)
    {
        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppConnectionRepository repository = new(context);

        Versioned<WhatsAppConnection> stored = (await repository
            .FindAsync(connection.TenantId, connection.Id))!;

        stored.Value.RecordCredentialStored(Noon);
        stored.Value.Activate(Noon);

        await repository.UpdateAsync(stored.Value, stored.Version);

        return stored.Value;
    }

    private async Task<Conversation> NewConversationAsync(Tenant tenant)
    {
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WhatsApp, Noon);

        await using OmniAiDbContext context = database.CreateContext();

        context.AddOwner(conversation);
        context.Conversations.Add(conversation);

        await context.SaveChangesAsync();

        return conversation;
    }

    private async Task<MessageId> AppendMessageAsync(Conversation conversation)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Conversation tracked = await context.Conversations
            .Include(candidate => candidate.Messages)
            .SingleAsync(candidate => candidate.Id == conversation.Id);

        Message message = tracked.AddTextMessage(
            MessageSender.AiAgent,
            "Thanks for getting in touch.",
            Noon);

        await context.SaveChangesAsync();

        return message.Id;
    }
}
