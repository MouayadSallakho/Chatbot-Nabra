using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Tenants;
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
public sealed class ListAiAgentsHandler(IAiAgentRepository agents, TenantAccessGuard access)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    /// <summary>Reads the page.</summary>
    /// <returns>
    /// The requested page. A tenant the caller belongs to that owns no agents
    /// produces an empty page rather than a failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Phase 12 replaced this endpoint's disclosure rule, and it is worth
    /// being precise about why.</strong> Phase 11 answered an empty page for a
    /// tenant that did not exist, so that a caller could not tell a real tenant id
    /// from an invented one. With no authentication, that was the strongest
    /// available answer. It is now the weaker one: membership is the boundary, so
    /// a tenant the caller does not belong to and a tenant that was never created
    /// are both reported as not found, and existence is hidden behind a rule that
    /// does not depend on guessing at all. Keeping the empty page would have meant
    /// a signed-in stranger receiving a 200 for somebody else's workspace.
    /// </para>
    /// <para>
    /// Absence of agents remains a result rather than a failure: a workspace the
    /// caller owns and has not filled yet is an ordinary state and gets an empty
    /// page, with no exception and no error-level log.
    /// </para>
    /// </remarks>
    /// <exception cref="Tenants.TenantNotFoundException">
    /// The caller does not belong to this tenant -- whether it exists or not.
    /// </exception>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
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

        // A page is not a nullable thing, so this use case reports the refusal the
        // only other way a not-found can travel out of the Application: the same
        // exception a create would raise for a tenant that is not there. The API
        // turns both into the same 404, so the two are indistinguishable on the
        // wire as well as here.
        if (!await _access.IsCurrentUserMemberAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        return await _agents
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
