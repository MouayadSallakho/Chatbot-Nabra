namespace OmniAI.Application.AiAgents.ListAiAgents;

/// <summary>
/// Request for one page of a tenant's AI agents.
/// </summary>
/// <param name="TenantId">The tenant whose agents to list, from the route.</param>
/// <param name="Page">
/// The 1-based page to read, or <see langword="null"/> to take the default.
/// </param>
/// <param name="PageSize">
/// How many agents the page may hold, or <see langword="null"/> to take the
/// default.
/// </param>
/// <remarks>
/// <para>
/// The tenant is part of the request, not an ambient value, and not optional.
/// Listing agents across tenants is not expressible, which is what keeps the
/// isolation rule out of reach of a forgotten call site.
/// </para>
/// <para>
/// Paging arguments are nullable so that "not supplied" is distinguishable from
/// "supplied as zero". A caller that omits them gets the defaults; a caller that
/// sends <c>?page=0</c> made a mistake and is told so, rather than having the
/// zero silently reinterpreted as one.
/// </para>
/// </remarks>
public sealed record ListAiAgentsQuery(Guid TenantId, int? Page = null, int? PageSize = null);
