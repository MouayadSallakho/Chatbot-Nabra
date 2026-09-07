using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Website chat endpoints, in PostgreSQL.
/// </summary>
internal sealed class WebChatEndpointRepository(OmniAiDbContext context)
    : IWebChatEndpointRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConcurrencyToken> AddAsync(
        WebChatEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        _context.WebChatEndpoints.Add(endpoint);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return VersionOf(endpoint);
    }

    public async Task<Versioned<WebChatEndpoint>?> FindAsync(
        TenantId tenantId,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default)
    {
        // The tenant is in the predicate, not checked afterwards. An endpoint id
        // from another workspace finds nothing rather than being found and then
        // rejected -- which is the difference between a lookup that cannot leak
        // and one that relies on a later line being right.
        WebChatEndpoint? endpoint = await _context.WebChatEndpoints
            .FirstOrDefaultAsync(
                candidate => candidate.Id == endpointId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return endpoint is null ? null : new Versioned<WebChatEndpoint>(endpoint, VersionOf(endpoint));
    }

    public async Task<IReadOnlyList<WebChatEndpointRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        // Joined rather than read in two passes, so the agent's name and status
        // are consistent with the endpoint row they are shown beside.
        List<WebChatEndpointRow> rows = await (
            from endpoint in _context.WebChatEndpoints
            join agent in _context.AiAgents on endpoint.AiAgentId equals agent.Id
            where endpoint.TenantId == tenantId
            orderby endpoint.CreatedAt descending
            select new WebChatEndpointRow(
                endpoint.Id.Value,
                agent.Id.Value,
                agent.Name.Value,
                agent.Status,
                endpoint.PublicKey.Value,
                endpoint.Status,
                endpoint.CreatedAt,
                endpoint.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    public async Task<ConcurrencyToken> UpdateAsync(
        WebChatEndpoint endpoint,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // The version the caller read is put back into the tracked entity, so EF
        // puts it in the UPDATE's WHERE clause. The database, not this process,
        // decides whether the row is still what the caller thought it was.
        RowVersion(endpoint).OriginalValue = Decode(expected);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new WebChatEndpointVersionMismatchException(expected);
        }

        return VersionOf(endpoint);
    }

    public async Task<PublicWebChatEndpoint?> FindByPublicKeyAsync(
        WebChatPublicKey publicKey,
        CancellationToken cancellationToken = default)
    {
        // The only query a public caller can cause, and it takes no tenant --
        // there is none to be given and none that could be believed. The workspace
        // comes back from the row, which is why a public URL cannot be edited into
        // a request against a different one.
        return await (
            from endpoint in _context.WebChatEndpoints
            join agent in _context.AiAgents on endpoint.AiAgentId equals agent.Id
            where endpoint.PublicKey == publicKey
            select new PublicWebChatEndpoint(
                endpoint.Id,
                endpoint.TenantId,
                agent.Id,
                agent.Name.Value,
                agent.Status,
                endpoint.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> ExistsForAgentAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default) =>
        _context.WebChatEndpoints.AnyAsync(
            endpoint => endpoint.TenantId == tenantId && endpoint.AiAgentId == aiAgentId,
            cancellationToken);

    /// <summary>The endpoint's current row version, as the platform's opaque token.</summary>
    private ConcurrencyToken VersionOf(WebChatEndpoint endpoint) =>
        RowVersionTokens.Encode(RowVersion(endpoint).CurrentValue);

    /// <summary>
    /// Reads a caller-supplied token back into the number PostgreSQL stores.
    /// </summary>
    /// <remarks>
    /// A token that is not one this platform issued cannot match any row, so it is
    /// treated as a mismatch rather than as a malformed request. The caller is
    /// told exactly what they would be told if somebody had genuinely changed the
    /// endpoint first, which discloses nothing about what a real token looks like.
    /// </remarks>
    private static uint Decode(ConcurrencyToken expected) =>
        RowVersionTokens.TryDecode(expected, out uint rowVersion) ? rowVersion : 0u;

    private Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<WebChatEndpoint, uint> RowVersion(
        WebChatEndpoint endpoint) =>
        _context.Entry(endpoint).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
