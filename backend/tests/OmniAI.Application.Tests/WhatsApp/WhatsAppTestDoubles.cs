using OmniAI.Application.Concurrency;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.Tests.WhatsApp;

/// <summary>
/// A WhatsApp world held in memory.
/// </summary>
/// <remarks>
/// <strong>It enforces the same uniqueness the database does.</strong> One
/// record per provider message id, one delivery per internal message. Those are
/// not conveniences: they are the two rules that stop a customer being answered
/// twice, and a double that did not enforce them would let a test pass while the
/// behaviour it claims to prove was absent.
/// <para>
/// Concurrency is what it cannot reproduce, which is why the same properties are
/// asserted again in the integration suite against real PostgreSQL.
/// </para>
/// </remarks>
internal sealed class WhatsAppWorld : IWhatsAppConnectionRepository, IWhatsAppInbox,
    IWhatsAppOutbox, IWhatsAppContactStore
{
    private readonly Dictionary<WhatsAppConnectionId, WhatsAppConnection> _connections = [];
    private readonly Dictionary<WhatsAppConnectionId, byte[]> _credentials = [];
    private readonly List<InboundWhatsAppMessage> _inbound = [];
    private readonly List<WhatsAppDelivery> _deliveries = [];
    private readonly List<WhatsAppContact> _contacts = [];

    /// <summary>Agent names, by id, as the list query would read them.</summary>
    public Dictionary<AiAgentId, string> AgentNames { get; } = [];

    /// <summary>Every inbound record, in the order it was written.</summary>
    public IReadOnlyList<InboundWhatsAppMessage> Inbound => _inbound;

    /// <summary>Every delivery, in the order it was queued.</summary>
    public IReadOnlyList<WhatsAppDelivery> Deliveries => _deliveries;

    /// <summary>Every contact mapping ever created.</summary>
    public IReadOnlyList<WhatsAppContact> Contacts => _contacts;

    /// <summary>Puts a connection into the world, already serving.</summary>
    public WhatsAppConnection Serving(
        TenantId tenantId,
        AiAgentId agentId,
        string phoneNumberId,
        DateTimeOffset now)
    {
        WhatsAppConnection connection = WhatsAppConnection.Create(
            tenantId,
            agentId,
            WabaId.Create("111222333444555"),
            PhoneNumberId.Create(phoneNumberId),
            "+44 7700 900000",
            now);

        connection.RecordCredentialStored(now);
        connection.Activate(now);

        _connections[connection.Id] = connection;
        _credentials[connection.Id] = [1, 2, 3];

        return connection;
    }

    /// <summary>Puts a connection into the world that is switched off.</summary>
    public WhatsAppConnection Draft(
        TenantId tenantId,
        AiAgentId agentId,
        string phoneNumberId,
        DateTimeOffset now)
    {
        WhatsAppConnection connection = WhatsAppConnection.Create(
            tenantId,
            agentId,
            WabaId.Create("111222333444555"),
            PhoneNumberId.Create(phoneNumberId),
            null,
            now);

        _connections[connection.Id] = connection;

        return connection;
    }

    public Task<IReadOnlyList<WhatsAppConnectionRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WhatsAppConnectionRow>>(
        [
            .. _connections.Values
                .Where(connection => connection.TenantId == tenantId)
                .Select(connection => new WhatsAppConnectionRow(
                    connection.Id.Value,
                    connection.AiAgentId.Value,
                    AgentNames.GetValueOrDefault(connection.AiAgentId),
                    connection.WabaId.Value,
                    connection.PhoneNumberId.Value,
                    connection.DisplayPhoneNumber,
                    connection.Status,
                    connection.HasCredential,
                    connection.CreatedAt,
                    connection.UpdatedAt)),
        ]);

    public Task<Versioned<WhatsAppConnection>?> FindAsync(
        TenantId tenantId,
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _connections.TryGetValue(connectionId, out WhatsAppConnection? connection)
            && connection.TenantId == tenantId
                ? new Versioned<WhatsAppConnection>(connection, ConcurrencyToken.From("1"))
                : null);

    public Task<string?> FindAgentNameAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentNames.GetValueOrDefault(aiAgentId));

    public Task<ConcurrencyToken> AddAsync(
        WhatsAppConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (_connections.Values.Any(other =>
            other.PhoneNumberId == connection.PhoneNumberId && other.Id != connection.Id))
        {
            throw new WhatsAppPhoneNumberInUseException();
        }

        _connections[connection.Id] = connection;

        return Task.FromResult(ConcurrencyToken.From("1"));
    }

    public Task<ConcurrencyToken> UpdateAsync(
        WhatsAppConnection connection,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default) =>
        AddAsync(connection, cancellationToken);

    public Task<ConcurrencyToken> ReplaceCredentialAsync(
        WhatsAppConnection connection,
        byte[] protectedToken,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connections[connection.Id] = connection;
        _credentials[connection.Id] = protectedToken;

        return Task.FromResult(ConcurrencyToken.From("1"));
    }

    public Task<byte[]?> FindProtectedCredentialAsync(
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials.GetValueOrDefault(connectionId));

    public Task<WhatsAppRoute?> FindRouteAsync(
        PhoneNumberId phoneNumberId,
        CancellationToken cancellationToken = default)
    {
        WhatsAppConnection? connection = _connections.Values
            .FirstOrDefault(candidate => candidate.PhoneNumberId == phoneNumberId);

        return Task.FromResult(connection is null
            ? null
            : new WhatsAppRoute(
                connection.Id,
                connection.TenantId,
                connection.AiAgentId,
                connection.IsServing));
    }

    /// <summary>Claims inbound work, for a test driving the worker's half.</summary>
    public Task<IReadOnlyList<ClaimedInboundMessage>> ClaimInboundAsync(
        int batchSize = 10,
        int maxAttempts = 5,
        DateTimeOffset now = default) =>
        ((IWhatsAppInbox)this).ClaimAsync(batchSize, maxAttempts, now, CancellationToken.None);

    /// <summary>Claims outbound work, for a test driving the dispatcher's half.</summary>
    public Task<IReadOnlyList<ClaimedDelivery>> ClaimDeliveriesAsync(
        int batchSize = 10,
        DateTimeOffset now = default) =>
        ((IWhatsAppOutbox)this).ClaimAsync(batchSize, 1, now, CancellationToken.None);

    public Task<bool> RecordAsync(
        InboundWhatsAppMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The unique index, in memory. Without it these tests would prove
        // nothing about redelivery.
        if (_inbound.Any(stored =>
            stored.ConnectionId == message.ConnectionId
            && stored.ProviderMessageId == message.ProviderMessageId))
        {
            return Task.FromResult(false);
        }

        _inbound.Add(message);

        return Task.FromResult(true);
    }

    /// <summary>Claims waiting inbound messages.</summary>
    /// <remarks>
    /// Explicit, because the inbox and the outbox declare claim methods with the
    /// same parameters and different return types. C# will not let one class
    /// carry both implicitly, and naming them apart would mean this double no
    /// longer implements the interfaces the code under test is written against.
    /// </remarks>
    Task<IReadOnlyList<ClaimedInboundMessage>> IWhatsAppInbox.ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ClaimedInboundMessage> claimed = [];

        foreach (InboundWhatsAppMessage message in _inbound
            .Where(candidate => candidate.Status == InboundWhatsAppStatus.Pending
                && candidate.Attempts < maxAttempts
                && candidate.Text is not null)
            .Take(batchSize)
            .ToList())
        {
            message.Claim(now);

            claimed.Add(new ClaimedInboundMessage(
                message.Id,
                message.ConnectionId,
                message.TenantId,
                _connections[message.ConnectionId].PhoneNumberId,
                message.ProviderMessageId,
                message.CustomerId,
                message.Text!,
                message.SentAt,
                message.Attempts));
        }

        return Task.FromResult<IReadOnlyList<ClaimedInboundMessage>>(claimed);
    }

    public Task MarkProcessedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Settle(id, message => message.MarkProcessed(now));

    public Task MarkFailedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Settle(id, message => message.MarkFailed(now));

    public Task ReleaseAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Settle(id, message => message.Release(now));

    public Task<int> ReleaseStaleClaimsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default)
    {
        int released = 0;

        foreach (InboundWhatsAppMessage message in _inbound
            .Where(candidate => candidate.Status == InboundWhatsAppStatus.Claimed
                && candidate.UpdatedAt < claimedBefore))
        {
            message.Release(claimedBefore);
            released++;
        }

        return Task.FromResult(released);
    }

    public Task<bool> QueueAsync(
        WhatsAppDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (_deliveries.Any(stored => stored.MessageId == delivery.MessageId))
        {
            return Task.FromResult(false);
        }

        _deliveries.Add(delivery);

        return Task.FromResult(true);
    }

    /// <summary>Claims waiting deliveries. Explicit, for the same reason.</summary>
    Task<IReadOnlyList<ClaimedDelivery>> IWhatsAppOutbox.ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        List<ClaimedDelivery> claimed = [];

        foreach (WhatsAppDelivery delivery in _deliveries
            .Where(candidate => candidate.Status == WhatsAppDeliveryStatus.Pending
                && candidate.Attempts < maxAttempts)
            .Take(batchSize)
            .ToList())
        {
            delivery.Claim(now);

            claimed.Add(new ClaimedDelivery(
                delivery.Id,
                delivery.ConnectionId,
                delivery.TenantId,
                delivery.ConversationId,
                _connections[delivery.ConnectionId].PhoneNumberId,
                delivery.Recipient,
                delivery.Text));
        }

        return Task.FromResult<IReadOnlyList<ClaimedDelivery>>(claimed);
    }

    public Task MarkSentAsync(
        WhatsAppDeliveryId id,
        ProviderMessageId providerMessageId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleDelivery(id, delivery => delivery.MarkSent(providerMessageId, now));

    public Task MarkFailedAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleDelivery(id, delivery => delivery.MarkFailed(reason, now));

    public Task MarkUnknownAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleDelivery(id, delivery => delivery.MarkUnknown(reason, now));

    public Task<WhatsAppStatusOutcome> ApplyProviderStatusAsync(
        ProviderMessageId providerMessageId,
        WhatsAppDeliveryStatus reported,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        WhatsAppDelivery? delivery = _deliveries
            .FirstOrDefault(candidate => candidate.ProviderMessageId == providerMessageId);

        if (delivery is null)
        {
            return Task.FromResult(WhatsAppStatusOutcome.Unmatched);
        }

        bool changed = delivery.ApplyProviderStatus(reported, reason, at);

        return Task.FromResult(new WhatsAppStatusOutcome(
            Matched: true,
            changed,
            delivery.TenantId,
            delivery.ConversationId));
    }

    public Task<int> AbandonStaleSendsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default)
    {
        int abandoned = 0;

        foreach (WhatsAppDelivery delivery in _deliveries
            .Where(candidate => candidate.Status == WhatsAppDeliveryStatus.Sending
                && candidate.UpdatedAt < claimedBefore))
        {
            delivery.MarkUnknown("abandoned", claimedBefore);
            abandoned++;
        }

        return Task.FromResult(abandoned);
    }

    public Task<IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus>> ListStatusesAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus>>(
            _deliveries
                .Where(delivery => delivery.ConversationId == conversationId)
                .ToDictionary(delivery => delivery.MessageId.Value, delivery => delivery.Status));

    public Task<ConversationId> ResolveConversationAsync(
        WhatsAppRoute route,
        WhatsAppCustomerId customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        WhatsAppContact? current = _contacts.FirstOrDefault(contact =>
            contact.ConnectionId == route.ConnectionId
            && contact.CustomerId == customerId
            && contact.IsCurrent);

        if (current is not null && !ClosedConversations.Contains(current.ConversationId))
        {
            return Task.FromResult(current.ConversationId);
        }

        current?.Supersede();

        ConversationId conversationId = NextConversation?.Invoke() ?? ConversationId.New();

        _contacts.Add(WhatsAppContact.Start(
            route.ConnectionId,
            route.TenantId,
            customerId,
            conversationId,
            now));

        return Task.FromResult(conversationId);
    }

    /// <summary>Conversations the world treats as closed.</summary>
    public HashSet<ConversationId> ClosedConversations { get; } = [];

    /// <summary>
    /// Where a new mapping's conversation comes from.
    /// </summary>
    /// <remarks>
    /// Set by a test that also holds a conversation store, so the id this world
    /// hands out names a conversation the runtime can actually find. Without it
    /// the two halves of the scenario would each have their own idea of which
    /// conversations exist.
    /// </remarks>
    public Func<ConversationId>? NextConversation { get; set; }

    public Task<WhatsAppConversationLink?> FindLinkAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        WhatsAppContact? contact = _contacts
            .FirstOrDefault(candidate => candidate.ConversationId == conversationId);

        if (contact is null)
        {
            return Task.FromResult<WhatsAppConversationLink?>(null);
        }

        WhatsAppConnection connection = _connections[contact.ConnectionId];

        return Task.FromResult<WhatsAppConversationLink?>(new WhatsAppConversationLink(
            contact.ConnectionId,
            contact.TenantId,
            contact.CustomerId,
            connection.PhoneNumberId,
            connection.IsServing));
    }

    private Task Settle(InboundWhatsAppMessageId id, Action<InboundWhatsAppMessage> change)
    {
        InboundWhatsAppMessage? message = _inbound.FirstOrDefault(candidate => candidate.Id == id);

        if (message is { Status: InboundWhatsAppStatus.Claimed })
        {
            change(message);
        }

        return Task.CompletedTask;
    }

    private Task SettleDelivery(WhatsAppDeliveryId id, Action<WhatsAppDelivery> change)
    {
        WhatsAppDelivery? delivery = _deliveries.FirstOrDefault(candidate => candidate.Id == id);

        if (delivery is not null
            && delivery.Status is WhatsAppDeliveryStatus.Pending or WhatsAppDeliveryStatus.Sending)
        {
            change(delivery);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// A sender that records what it was asked to send.
/// </summary>
/// <remarks>
/// <strong>There is no network here and no way to reach one.</strong> These tests
/// assert what the platform does with each of the three outcomes, which is a
/// question about the platform rather than about Meta.
/// </remarks>
internal sealed class RecordingWhatsAppSender : IWhatsAppMessageSender
{
    /// <summary>Every send, in order.</summary>
    public List<(PhoneNumberId From, WhatsAppCustomerId To, string Text, string Token)> Sent
    { get; } = [];

    /// <summary>What the next send returns.</summary>
    public WhatsAppSendResult Result { get; set; } =
        WhatsAppSendResult.Accepted(ProviderMessageId.Create("wamid.SENT=="));

    public Task<WhatsAppSendResult> SendTextAsync(
        PhoneNumberId sendFrom,
        WhatsAppCustomerId recipient,
        string text,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        Sent.Add((sendFrom, recipient, text, accessToken));

        return Task.FromResult(Result);
    }
}
