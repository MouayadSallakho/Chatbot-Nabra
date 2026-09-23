using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Which conversation a WhatsApp customer is talking in, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>A person messaging a business is one continuing conversation, until
/// somebody ends it.</strong> The mapping from a customer to a conversation is
/// therefore not a lookup table that grows a row per message; it is at most one
/// current row per customer per connection, superseded when the conversation is
/// closed and a new one begins.
/// <para>
/// <strong>The race is real and is settled in the database.</strong> Two messages
/// sent a second apart arrive as two webhooks that can be processed at the same
/// moment by two workers. Both would find no mapping and both would start a
/// conversation, and the customer would be answered twice with no shared history.
/// The partial unique index on the current mapping lets exactly one of them win;
/// the other rolls back and reads what the winner wrote.
/// </para>
/// </remarks>
internal sealed class WhatsAppContactStore(OmniAiDbContext context) : IWhatsAppContactStore
{
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    /// <summary>
    /// Declared in the migration, not in the model: it is unique only where
    /// <c>is_current</c>, and EF cannot express the filter.
    /// </summary>
    private const string CurrentContactIndex = "ux_whatsapp_contacts_current";

    /// <summary>
    /// The primary key on (workspace, sender) that makes one person one customer.
    /// </summary>
    /// <remarks>
    /// Two first messages from the same number racing each other both try to
    /// insert the identity. One wins; the other reads what the winner wrote, which
    /// is why there is no application lock here and why a second instance of the
    /// worker changes nothing.
    /// </remarks>
    private const string CustomerIdentityKey = "PK_whatsapp_customer_identities";

    /// <summary>
    /// How many times a lost race is re-run before giving up.
    /// </summary>
    /// <remarks>
    /// Two is enough for the race this guards: losing means somebody else has
    /// just committed the mapping, so the re-read finds it. A third failure is
    /// not contention, and looping on it would turn a bug into a hang.
    /// </remarks>
    private const int Attempts = 3;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConversationId> ResolveConversationAsync(
        WhatsAppRoute route,
        WhatsAppCustomerId customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        // Who this is, decided before and outside the conversation transaction.
        // Identity is durable and independent of whether this particular
        // conversation starts, and resolving it inside that transaction would mean
        // a lost identity race aborted the conversation's transaction too --
        // PostgreSQL gives up on a transaction the moment a statement in it fails.
        CustomerId customer = await ResolveCustomerAsync(
                route.TenantId,
                customerId,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        for (int attempt = 1; ; attempt++)
        {
            WhatsAppContact? current = await CurrentAsync(
                    route.ConnectionId,
                    customerId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (current is not null)
            {
                ConversationStatus status = await _context.Conversations
                    .AsNoTracking()
                    .Where(conversation => conversation.Id == current.ConversationId)
                    .Select(conversation => conversation.Status)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (status == ConversationStatus.Open)
                {
                    return current.ConversationId;
                }
            }

            try
            {
                return await StartAsync(
                        route,
                        customerId,
                        customer,
                        current,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateException failure)
                when (IsContactRace(failure) && attempt < Attempts)
            {
                // Somebody else mapped this customer first. Their conversation is
                // the one the customer is in, so the next pass reads it rather
                // than insisting on the one this attempt was building.
                _context.ChangeTracker.Clear();
            }
        }
    }

    public async Task<WhatsAppConversationLink?> FindLinkAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        // Not filtered on the mapping being current. A conversation that has been
        // superseded is still that customer's conversation, and a colleague
        // reading it in the inbox must still be able to answer them.
        return await (
            from contact in _context.WhatsAppContacts.AsNoTracking()
            join connection in _context.WhatsAppConnections
                on contact.ConnectionId equals connection.Id
            where contact.ConversationId == conversationId
            select new WhatsAppConversationLink(
                contact.ConnectionId,
                contact.TenantId,
                contact.CustomerId,
                connection.PhoneNumberId,
                connection.Status == WhatsAppConnectionStatus.Active && connection.HasCredential))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a conversation for this customer and maps them to it.
    /// </summary>
    /// <remarks>
    /// One transaction, because a conversation nobody is mapped to is invisible
    /// and a mapping to a conversation that does not exist is unusable. Either
    /// both land or neither does.
    /// </remarks>
    private async Task<ConversationId> StartAsync(
        WhatsAppRoute route,
        WhatsAppCustomerId customerId,
        CustomerId customer,
        WhatsAppContact? superseded,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Retired first, in the same statement batch as the insert, because the
        // index allows only one current mapping and the new row is about to be it.
        superseded?.Supersede();

        Conversation conversation = Conversation.Start(
            route.TenantId,
            customer,
            ChannelKind.WhatsApp,
            now);

        _context.Conversations.Add(conversation);

        _context.WhatsAppContacts.Add(WhatsAppContact.Start(
            route.ConnectionId,
            route.TenantId,
            customerId,
            conversation.Id,
            now));

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            throw;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return conversation.Id;
    }

    /// <summary>
    /// The canonical customer behind one WhatsApp number, creating one the first
    /// time that number is seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the only automatic identity claim the platform makes, and
    /// it is a transport fact rather than a guess.</strong> Meta reports the
    /// sender; the same sender in the same workspace is the same person. Nothing
    /// here compares names, compares numbers for similarity, or reasons about
    /// devices — if the transport cannot prove it, two identities stay two people
    /// until an administrator says otherwise.
    /// </para>
    /// <para>
    /// Keyed on the workspace rather than on the connection, so somebody who
    /// messages two of a workspace's numbers is one customer. That is still the
    /// same transport fact: it is the sender Meta names, not two strings that
    /// happen to look alike.
    /// </para>
    /// <para>
    /// The insert races with itself and settles in the database. The primary key
    /// admits one row; the loser reads the winner's and uses it, which is what
    /// makes two simultaneous first messages produce one customer without any
    /// process-local lock.
    /// </para>
    /// </remarks>
    private async Task<CustomerId> ResolveCustomerAsync(
        TenantId tenantId,
        WhatsAppCustomerId customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CustomerId? existing = await FindCustomerAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is CustomerId known)
        {
            return known;
        }

        // No label. A number is not a name, and inventing one from the provider's
        // profile would put an unverified claim about a real person on screen as
        // though somebody had checked it.
        Customer customer = Customer.Create(tenantId, displayName: null, now);

        _context.Customers.Add(customer);

        _context.WhatsAppCustomerIdentities.Add(WhatsAppCustomerIdentity.Create(
            tenantId,
            customerId,
            customer.Id,
            now));

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return customer.Id;
        }
        catch (DbUpdateException failure) when (IsIdentityRace(failure))
        {
            // Somebody else recorded this person first. Theirs is the identity,
            // and the one built here is discarded rather than retried: two
            // customers for one number is the exact failure this guards.
            _context.Entry(customer).State = EntityState.Detached;

            foreach (EntityEntry entry in _context.ChangeTracker
                .Entries<WhatsAppCustomerIdentity>()
                .Where(tracked => tracked.State == EntityState.Added)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }

            CustomerId? winner = await FindCustomerAsync(tenantId, customerId, cancellationToken)
                .ConfigureAwait(false);

            // No winner means the violation was not the one this catch claims to
            // handle, so the original failure is the honest answer.
            if (winner is null)
            {
                throw;
            }

            return winner.Value;
        }
    }

    private async Task<CustomerId?> FindCustomerAsync(
        TenantId tenantId,
        WhatsAppCustomerId customerId,
        CancellationToken cancellationToken)
    {
        List<CustomerId> found = await _context.WhatsAppCustomerIdentities
            .AsNoTracking()
            .Where(identity =>
                identity.TenantId == tenantId && identity.WhatsAppCustomerId == customerId)
            .Select(identity => identity.CustomerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return found.Count == 0 ? null : found[0];
    }

    /// <summary>Whether the save lost the race for this person's identity.</summary>
    private static bool IsIdentityRace(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == CustomerIdentityKey;

    private Task<WhatsAppContact?> CurrentAsync(
        WhatsAppConnectionId connectionId,
        WhatsAppCustomerId customerId,
        CancellationToken cancellationToken) =>
        _context.WhatsAppContacts.FirstOrDefaultAsync(
            contact => contact.ConnectionId == connectionId
                && contact.CustomerId == customerId
                && contact.IsCurrent,
            cancellationToken);

    /// <summary>Whether the save lost the race for this customer's mapping.</summary>
    private static bool IsContactRace(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == CurrentContactIndex;
}
