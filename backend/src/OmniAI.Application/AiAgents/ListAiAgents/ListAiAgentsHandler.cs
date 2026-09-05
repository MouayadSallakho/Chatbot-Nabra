using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.ListAiAgents;

/// <summary>
/// Reads one page of a tenant's AI agents.
/// </summary>
/// <remarks>
/// The whole use case is: check the paging arguments, convert the tenant, ask
/// storage. There is no filtering, no searching, no sorting parameter and no
/// projection choice, because none of those has a caller yet -- and a query
/// surface invented before its first real requirement is a surface that has to
/// be supported before anybody knows whether it was right.
/// </remarks>
public sealed class ListAiAgentsHandler(IAiAgentRepository agents)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    /// <summary>Reads the page.</summary>
    /// <returns>
    /// The requested page. A tenant with no agents -- and a tenant that does not
    /// exist at all -- both produce an empty page rather than a failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>No check that the tenant exists</strong>, for the same reason the
    /// single-agent read has none: a request naming a tenant that is not there
    /// finds nothing, and is answered exactly like a request naming a real tenant
    /// with no agents. Adding an existence check would make those two
    /// distinguishable and turn this endpoint into a way to discover which tenant
    /// ids are real.
    /// </para>
    /// <para>
    /// Absence is a result, not a failure: no exception, no stack unwind, no
    /// error-level log for the ordinary case of a new workspace.
    /// </para>
    /// </remarks>
    /// <exception cref="AiAgentPageValidationException">
    /// The page or page size is outside the range this platform serves.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">
    /// The tenant identifier is empty -- malformed input rather than a tenant
    /// that happens to own nothing.
    /// </exception>
    public async Task<AiAgentPage> HandleAsync(
        ListAiAgentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page ?? AiAgentPage.DefaultPage;
        int pageSize = query.PageSize ?? AiAgentPage.DefaultPageSize;

        if (page < AiAgentPage.DefaultPage)
        {
            throw AiAgentPageValidationException.ForPage(page);
        }

        if (pageSize < 1 || pageSize > AiAgentPage.MaxPageSize)
        {
            throw AiAgentPageValidationException.ForPageSize(pageSize);
        }

        TenantId tenantId = TenantId.From(query.TenantId);

        return await _agents
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
