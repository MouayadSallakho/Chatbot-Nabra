using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// WhatsApp connections, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>The credential lives in its own table and is read by exactly one
/// method here.</strong> Everything else — the list, the detail, the routing
/// lookup the webhook makes for every message — reads the connection row, which
/// holds no token at all.
/// <para>
/// Uniqueness of the phone number id is deployment-wide. That is unusual for a
/// multi-workspace platform and deliberate: routing an inbound message is done by
/// phone number id alone, so two workspaces claiming one number would make the
/// answer ambiguous, and an ambiguous answer here is one company's customers
/// landing in another company's inbox.
/// </para>
/// </remarks>
internal sealed class WhatsAppConnectionRepository(OmniAiDbContext context)
    : IWhatsAppConnectionRepository
{
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    private const string PhoneNumberIndex = "ux_whatsapp_connections_phone_number";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<WhatsAppConnectionRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        // Left-joined to the agent, so a connection whose agent was deleted still
        // appears -- unnamed, but visible and therefore fixable. An inner join
        // would make it vanish from the only screen that could repair it.
        List<WhatsAppConnectionRow> rows = await (
            from connection in _context.WhatsAppConnections
            join agent in _context.AiAgents
                on connection.AiAgentId equals agent.Id into candidates
            from agent in candidates.DefaultIfEmpty()
            where connection.TenantId == tenantId
            orderby connection.CreatedAt descending
            select new WhatsAppConnectionRow(
                connection.Id.Value,
                connection.AiAgentId.Value,
                agent == null ? null : agent.Name.Value,
                connection.WabaId.Value,
                connection.PhoneNumberId.Value,
                connection.DisplayPhoneNumber,
                connection.Status,
                connection.HasCredential,
                connection.CreatedAt,
                connection.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    public async Task<Versioned<WhatsAppConnection>?> FindAsync(
        TenantId tenantId,
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default)
    {
        // The workspace is part of the predicate rather than a check afterwards.
        // An id belonging to somebody else finds nothing, which is a lookup that
        // cannot leak rather than one that depends on a later line being right.
        WhatsAppConnection? connection = await _context.WhatsAppConnections
            .FirstOrDefaultAsync(
                candidate => candidate.Id == connectionId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return connection is null
            ? null
            : new Versioned<WhatsAppConnection>(connection, VersionOf(connection));
    }

    public async Task<string?> FindAgentNameAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AiAgents
            .Where(agent => agent.Id == aiAgentId && agent.TenantId == tenantId)
            .Select(agent => (string?)agent.Name.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConcurrencyToken> AddAsync(
        WhatsAppConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _context.WhatsAppConnections.Add(connection);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsPhoneNumberTaken(failure))
        {
            _context.Entry(connection).State = EntityState.Detached;

            throw new WhatsAppPhoneNumberInUseException();
        }

        return VersionOf(connection);
    }

    public async Task<ConcurrencyToken> UpdateAsync(
        WhatsAppConnection connection,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The version the caller read goes back into the tracked entity, so it
        // lands in the UPDATE's WHERE clause and the database decides whether the
        // row is still what the caller believed.
        RowVersion(connection).OriginalValue = Decode(expected);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new WhatsAppConnectionVersionMismatchException(expected);
        }
        catch (DbUpdateException failure) when (IsPhoneNumberTaken(failure))
        {
            throw new WhatsAppPhoneNumberInUseException();
        }

        return VersionOf(connection);
    }

    public async Task<ConcurrencyToken> ReplaceCredentialAsync(
        WhatsAppConnection connection,
        byte[] protectedToken,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(protectedToken);

        // <strong>One transaction for two rows.</strong> The ciphertext and the
        // "a credential exists" flag are one fact stated in two places; a crash
        // between them would leave a connection that promises a token it does not
        // have, and every send would fail with an error nobody could explain.
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        WhatsAppCredentialRow? existing = await _context.WhatsAppCredentials
            .FirstOrDefaultAsync(row => row.ConnectionId == connection.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _context.WhatsAppCredentials.Add(new WhatsAppCredentialRow
            {
                ConnectionId = connection.Id,
                ProtectedToken = protectedToken,
                UpdatedAt = connection.UpdatedAt,
            });
        }
        else
        {
            existing.ProtectedToken = protectedToken;
            existing.UpdatedAt = connection.UpdatedAt;
        }

        RowVersion(connection).OriginalValue = Decode(expected);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            throw new WhatsAppConnectionVersionMismatchException(expected);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return VersionOf(connection);
    }

    public async Task<byte[]?> FindProtectedCredentialAsync(
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default)
    {
        // No workspace in the predicate, and that is not an oversight: the caller
        // is the outbound worker, which has no user and no session, acting on a
        // delivery row whose workspace the platform itself wrote. The ciphertext
        // is bound during encryption to the workspace and the connection, so bytes
        // fetched for the wrong one decrypt to nothing rather than to a usable
        // token.
        return await _context.WhatsAppCredentials
            .AsNoTracking()
            .Where(row => row.ConnectionId == connectionId)
            .Select(row => row.ProtectedToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WhatsAppRoute?> FindRouteAsync(
        PhoneNumberId phoneNumberId,
        CancellationToken cancellationToken = default)
    {
        // <strong>The only routing question the webhook may ask.</strong> It takes
        // no workspace, because the caller has none to offer that could be
        // believed: the request is a POST from Meta, and every workspace-like
        // field inside its body is attacker-influenced. The workspace comes back
        // from the row.
        return await _context.WhatsAppConnections
            .AsNoTracking()
            .Where(connection => connection.PhoneNumberId == phoneNumberId)
            .Select(connection => new WhatsAppRoute(
                connection.Id,
                connection.TenantId,
                connection.AiAgentId,
                connection.Status == WhatsAppConnectionStatus.Active && connection.HasCredential))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a failed save was the phone number index refusing a duplicate.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name rather than on "a unique violation
    /// happened". Another broken constraint is a different problem, and reporting
    /// it as "that number is already connected" would send somebody looking in
    /// the wrong place.
    /// </remarks>
    private static bool IsPhoneNumberTaken(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == PhoneNumberIndex;

    /// <summary>The connection's current row version, as the platform's token.</summary>
    private ConcurrencyToken VersionOf(WhatsAppConnection connection) =>
        RowVersionTokens.Encode(RowVersion(connection).CurrentValue);

    /// <summary>
    /// Reads a caller-supplied token back into the number PostgreSQL stores.
    /// </summary>
    /// <remarks>
    /// A token this platform did not issue cannot match any row, so it is treated
    /// as a stale version rather than as a malformed request: the caller is told
    /// exactly what somebody who genuinely lost a race is told, which discloses
    /// nothing about what a real token looks like.
    /// </remarks>
    private static uint Decode(ConcurrencyToken expected) =>
        RowVersionTokens.TryDecode(expected, out uint rowVersion) ? rowVersion : 0u;

    private PropertyEntry<WhatsAppConnection, uint> RowVersion(WhatsAppConnection connection) =>
        _context.Entry(connection).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
