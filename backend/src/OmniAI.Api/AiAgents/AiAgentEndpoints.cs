using OmniAI.Api.Http;
using OmniAI.Api.Knowledge;
using OmniAI.Api.Security;
using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.PreviewAiAgent;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.AgentKnowledge;
using OmniAI.Application.Knowledge.SearchAgentKnowledge;

namespace OmniAI.Api.AiAgents;

/// <summary>
/// The AI agent HTTP surface, nested under the tenant that owns it.
/// </summary>
/// <remarks>
/// Minimal API endpoint mapping, matching the style the tenant endpoints use. No
/// controller is introduced: mixing two API styles in one application means two
/// places to look for routing, filters and model binding.
/// <para>
/// Each endpoint does much the same four things -- read the precondition,
/// translate the request, call one handler, translate the outcome. There is no
/// business logic here, no database call, and no error mapping: expected failures
/// are translated centrally by
/// <see cref="ErrorHandling.DomainProblemDetailsHandler"/>, so the mapping exists
/// once rather than once per endpoint. That is what keeps six endpoints from
/// becoming a fat class.
/// </para>
/// <para>
/// <strong>Every route carries the owning tenant, and it is not decoration.</strong>
/// The tenant reaches the repository and lands in the SQL WHERE clause, so an
/// agent belonging to another tenant is never read. A caller who knows a valid
/// agent id -- or a valid entity tag for it -- still cannot reach it through a
/// route naming a different tenant: the answer is the same not-found they would
/// get for an id that does not exist anywhere, and no response distinguishes the
/// two.
/// </para>
/// <para>
/// <strong>Every mutation is conditional.</strong> The four mutating endpoints
/// require <c>If-Match</c>, and refuse to act without it. See
/// <see cref="EntityTags"/> for the exact contract and
/// <c>docs/architecture/ai-agent-vertical-slice.md</c> for why client-think-time
/// concurrency needs it even though the database already has a concurrency
/// token.
/// </para>
/// <para>
/// <strong>Three questions are answered before any of these endpoints does its
/// work, and they produce three different statuses.</strong> Is anybody signed in
/// -- <c>401</c> from the pipeline. Does that person belong to the tenant in the
/// route -- <c>404</c>, the same answer as for a tenant that never existed, so
/// this route cannot be used to discover which workspaces are real. Do their
/// roles carry the permission this operation needs -- <c>403</c>, which names the
/// permission, because somebody who has already proved they belong here can act
/// on that. The last two are decided inside each use case rather than here: an
/// authorization attribute on an endpoint proves only that somebody is signed in.
/// </para>
/// <para>
/// This note replaces a Phase 10 one saying these endpoints were unauthenticated.
/// It was already stale after Phase 12 and would have been read as current.
/// </para>
/// </remarks>
internal static class AiAgentEndpoints
{
    /// <summary>Route name for the single-agent read endpoint.</summary>
    /// <remarks>
    /// Used to generate the <c>Location</c> header of a successful creation, so
    /// the header and the route cannot drift apart the way a hand-built path
    /// string would.
    /// </remarks>
    private const string GetAiAgentByIdRouteName = "GetAiAgentById";

    public static IEndpointRouteBuilder MapAiAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder agents = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/ai-agents")
            .WithTags("AI agents")
            // Applied to the group, so a route added later inherits both and
            // cannot be published unprotected by omission. Authorization answers
            // 401 for an anonymous caller; membership is checked below it, in
            // each use case, and answers 404 for a signed-in stranger.
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        agents.MapPost("/", CreateAsync)
            .WithName("CreateAiAgent")
            .Produces<AiAgentResponse>(StatusCodes.Status201Created);

        agents.MapGet("/", ListAsync)
            .WithName("ListAiAgents")
            .WithSummary("One page of the workspace's AI agents, each with the channels the reader may see.")
            .Produces<AiAgentPageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        agents.MapGet("/{aiAgentId:guid}", GetByIdAsync)
            .WithName(GetAiAgentByIdRouteName)
            .WithSummary("One AI agent with its ETag and the channels the reader may see.")
            .Produces<AiAgentDetailResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // PUT on a named sub-resource, not PATCH on the agent. Each of these
        // replaces one addressable thing whose whole value is in the body, which
        // is what PUT means; a PATCH would need the partial-update semantics this
        // contract deliberately does not define.
        agents.MapPut("/{aiAgentId:guid}/name", RenameAsync)
            .WithName("RenameAiAgent")
            .Produces<AiAgentResponse>(StatusCodes.Status200OK);

        agents.MapPut("/{aiAgentId:guid}/instructions", UpdateInstructionsAsync)
            .WithName("UpdateAiAgentInstructions")
            .Produces<AiAgentResponse>(StatusCodes.Status200OK);

        // POST, not PUT: activation and disabling are operations rather than
        // representations. There is nothing to send and nothing at these paths to
        // read back, so modelling them as a state resource would invent a
        // document that does not exist.
        agents.MapPost("/{aiAgentId:guid}/activate", ActivateAsync)
            .WithName("ActivateAiAgent")
            .Produces<AiAgentResponse>(StatusCodes.Status200OK);

        agents.MapPost("/{aiAgentId:guid}/disable", DisableAsync)
            .WithName("DisableAiAgent")
            .Produces<AiAgentResponse>(StatusCodes.Status200OK);

        // POST rather than GET, even though preview changes nothing on this
        // server. The request carries a body, and the body is the message -- a GET
        // would have to put somebody's text in the URL, where it lands in access
        // logs, proxy logs and browser history. Nor is it cacheable: the same
        // message may legitimately produce a different answer each time.
        agents.MapPost("/{aiAgentId:guid}/preview", PreviewAsync)
            .WithName("PreviewAiAgent")
            .Produces<AiAgentPreviewResponse>(StatusCodes.Status200OK);

        // Knowledge assignment lives under the agent rather than under knowledge,
        // because it is a property of the agent: it is edited on the agent's
        // screen, saved against the agent's entity tag, and moves the agent's
        // version. Putting it under /knowledge would have made the resource it
        // belongs to invisible in its own address.
        agents.MapGet("/{aiAgentId:guid}/knowledge", GetKnowledgeAsync)
            .WithName("GetAiAgentKnowledge");

        agents.MapPut("/{aiAgentId:guid}/knowledge", ReplaceKnowledgeAsync)
            .WithName("ReplaceAiAgentKnowledge")
            .Produces<AgentKnowledgeResponse>(StatusCodes.Status200OK);

        agents.MapPost("/{aiAgentId:guid}/knowledge/search", SearchKnowledgeAsync)
            .WithName("SearchAiAgentKnowledge")
            .Produces<KnowledgeSearchResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/ai-agents</c>. Returns <c>201 Created</c>
    /// with a <c>Location</c> header pointing at the read endpoint and an
    /// <c>ETag</c> for the version just stored.
    /// </summary>
    /// <remarks>
    /// No <c>If-Match</c>: there is no previous version of a resource that does
    /// not exist yet, so there is nothing to be out of date about. The <c>ETag</c>
    /// it returns is what lets a client immediately make a conditional write
    /// without reading the agent back.
    /// <para>
    /// A rejected name or instruction text surfaces as <c>400</c>, and an unknown
    /// tenant as <c>404</c>. Neither is caught here.
    /// </para>
    /// </remarks>
    private static async Task<IResult> CreateAsync(
        HttpContext http,
        Guid tenantId,
        CreateAiAgentRequest request,
        CreateAiAgentHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<AiAgentView> created = await handler
            .HandleAsync(
                new CreateAiAgentCommand(tenantId, request.Name, request.Instructions),
                cancellationToken)
            .ConfigureAwait(false);

        SetETag(http, created.Version);

        return Results.CreatedAtRoute(
            GetAiAgentByIdRouteName,
            new { tenantId = created.Value.TenantId, aiAgentId = created.Value.Id },
            AiAgentResponse.From(created.Value));
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/ai-agents</c>. Returns <c>200</c> with one
    /// page of the tenant's agents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No <c>ETag</c>, and no <c>404</c>.</strong> A page is not a
    /// resource a caller may write against, so there is no version to publish. A
    /// tenant with no agents and a tenant that does not exist both answer
    /// <c>200</c> with an empty page: making those distinguishable would turn this
    /// endpoint into a way to discover which tenant ids are real, and an empty
    /// list is the truthful answer to "what agents does this tenant have" in both
    /// cases.
    /// </para>
    /// <para>
    /// <c>page</c> and <c>pageSize</c> are optional and bound from the query
    /// string as nullable integers, so "not supplied" stays distinguishable from
    /// "supplied as zero". Out-of-range values are refused as <c>400</c> by the
    /// handler rather than clamped -- a caller that asked for five hundred and
    /// silently received a hundred cannot tell a truncated page from the end of
    /// the list.
    /// </para>
    /// <para>
    /// A value that is not an integer at all never reaches the handler: model
    /// binding rejects it, and the malformed-request arm of the Problem Details
    /// handler answers <c>400</c>.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        ListAiAgentsHandler handler,
        CancellationToken cancellationToken)
    {
        AiAgentPage agents = await handler
            .HandleAsync(new ListAiAgentsQuery(tenantId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(AiAgentPageResponse.From(agents));
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/ai-agents/{aiAgentId}</c>. Returns
    /// <c>200</c> with an <c>ETag</c>, or <c>404</c> as Problem Details.
    /// </summary>
    /// <remarks>
    /// No <c>If-Match</c>, and no <c>If-None-Match</c> either: reads change
    /// nothing, and conditional caching is not in scope. The <c>ETag</c> is here
    /// because it is what a client must send back to change the agent safely.
    /// </remarks>
    private static async Task<IResult> GetByIdAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        GetAiAgentByIdHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<AiAgentView>? agent = await handler
            .HandleAsync(new GetAiAgentByIdQuery(tenantId, aiAgentId), cancellationToken)
            .ConfigureAwait(false);

        if (agent is null)
        {
            return AgentNotFound(aiAgentId);
        }

        // The read alone carries the agent's channels (Phase 37.5a); the writes
        // keep their seven fields. See AiAgentDetailResponse.
        SetETag(http, agent.Version);

        return Results.Ok(AiAgentDetailResponse.From(agent.Value));
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/ai-agents/{aiAgentId}/name</c>. Requires
    /// <c>If-Match</c>. Returns <c>200</c> with the updated agent and its new
    /// <c>ETag</c>.
    /// </summary>
    private static async Task<IResult> RenameAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        RenameAiAgentRequest request,
        RenameAiAgentHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<AiAgentView>? renamed = await handler
            .HandleAsync(
                new RenameAiAgentCommand(tenantId, aiAgentId, request.Name, expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, renamed, aiAgentId);
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/ai-agents/{aiAgentId}/instructions</c>.
    /// Requires <c>If-Match</c>. Returns <c>200</c> with the updated agent.
    /// </summary>
    private static async Task<IResult> UpdateInstructionsAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        UpdateAiAgentInstructionsRequest request,
        UpdateAiAgentInstructionsHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<AiAgentView>? updated = await handler
            .HandleAsync(
                new UpdateAiAgentInstructionsCommand(tenantId, aiAgentId, request.Instructions, expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated, aiAgentId);
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/ai-agents/{aiAgentId}/activate</c>.
    /// Requires <c>If-Match</c>. Returns <c>200</c> with the updated agent.
    /// </summary>
    private static async Task<IResult> ActivateAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        ActivateAiAgentHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<AiAgentView>? activated = await handler
            .HandleAsync(new ActivateAiAgentCommand(tenantId, aiAgentId, expected), cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, activated, aiAgentId);
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/ai-agents/{aiAgentId}/disable</c>.
    /// Requires <c>If-Match</c>. Returns <c>200</c> with the updated agent, or
    /// <c>409</c> when the agent is still a draft.
    /// </summary>
    private static async Task<IResult> DisableAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        DisableAiAgentHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<AiAgentView>? disabled = await handler
            .HandleAsync(new DisableAiAgentCommand(tenantId, aiAgentId, expected), cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, disabled, aiAgentId);
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/ai-agents/{aiAgentId}/preview</c>. Returns
    /// <c>200</c> with the generated reply, or <c>404</c> as Problem Details.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No <c>If-Match</c>, and no <c>ETag</c> in the response.</strong>
    /// Preview reads the agent and changes nothing: no property is written,
    /// <c>updatedAt</c> does not move and the agent's version does not change.
    /// A precondition protects a caller from overwriting somebody else's edit, and
    /// there is no write here to protect. Requiring one would be ceremony that
    /// taught clients the wrong thing about what this endpoint does.
    /// </para>
    /// <para>
    /// <strong>Works for every lifecycle state.</strong> Draft, active and
    /// disabled agents all preview, because previewing is how somebody decides
    /// whether an agent is ready to activate, and how they check one they have
    /// taken out of service for repair. Status governs whether an agent serves
    /// customers, not whether its owner may test it.
    /// </para>
    /// <para>
    /// Failures are translated centrally: an unusable message becomes
    /// <c>400</c>, an unconfigured AI runtime <c>503</c>, and an unusable provider
    /// answer <c>502</c>. None of them is caught here -- see
    /// <see cref="ErrorHandling.DomainProblemDetailsHandler"/>.
    /// </para>
    /// </remarks>
    private static async Task<IResult> PreviewAsync(
        Guid tenantId,
        Guid aiAgentId,
        AiAgentPreviewRequest request,
        PreviewAiAgentHandler handler,
        CancellationToken cancellationToken)
    {
        AiAgentPreview? preview = await handler
            .HandleAsync(
                new PreviewAiAgentCommand(tenantId, aiAgentId, request.Message),
                cancellationToken)
            .ConfigureAwait(false);

        // The same not-found body every other agent operation returns, including
        // for an agent that exists under a different tenant. The generator was
        // never reached in that case, so nothing was sent anywhere before the
        // request was refused.
        return preview is null
            ? AgentNotFound(aiAgentId)
            : Results.Ok(AiAgentPreviewResponse.From(preview));
    }

    /// <summary>
    /// The response shared by the read and all four mutations: the agent with its
    /// <c>ETag</c>, or not-found.
    /// </summary>
    /// <remarks>
    /// One contract for every mutation, chosen over <c>204 No Content</c>. Each of
    /// these operations changes <c>updatedAt</c> and the version, and two change
    /// <c>status</c>, so a client that needs the result of its own request would
    /// otherwise have to issue a second <c>GET</c> to learn what happened -- and
    /// would meanwhile be showing state it inferred rather than state the server
    /// confirmed. It also matters for the next write: the response carries the new
    /// <c>ETag</c>, so a client can make consecutive edits without re-reading.
    /// <para>
    /// It is what makes an idempotent call informative too: activating an already
    /// active agent answers <c>200</c> with an unchanged <c>updatedAt</c> and an
    /// unchanged <c>ETag</c>, which is the truthful account of a request that
    /// changed nothing.
    /// </para>
    /// </remarks>
    /// <summary>Reads which documents this agent may draw on.</summary>
    /// <remarks>
    /// The entity tag it sets is the <em>agent's</em>, not one of its own, because
    /// that is the version the corresponding write is checked against.
    /// </remarks>
    private static async Task<IResult> GetKnowledgeAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        GetAgentKnowledgeHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<AgentKnowledgeView>? knowledge = await handler
            .HandleAsync(new GetAgentKnowledgeQuery(tenantId, aiAgentId), cancellationToken)
            .ConfigureAwait(false);

        if (knowledge is null)
        {
            return AgentNotFound(aiAgentId);
        }

        SetETag(http, knowledge.Version);

        return Results.Ok(AgentKnowledgeResponse.From(knowledge.Value));
    }

    /// <summary>Replaces which documents this agent may draw on.</summary>
    private static async Task<IResult> ReplaceKnowledgeAsync(
        HttpContext http,
        Guid tenantId,
        Guid aiAgentId,
        AgentKnowledgeRequest request,
        ReplaceAgentKnowledgeHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<AgentKnowledgeView>? replaced = await handler
            .HandleAsync(
                new ReplaceAgentKnowledgeCommand(
                    tenantId,
                    aiAgentId,
                    request.KnowledgeDocumentIds,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        if (replaced is null)
        {
            return AgentNotFound(aiAgentId);
        }

        SetETag(http, replaced.Version);

        return Results.Ok(AgentKnowledgeResponse.From(replaced.Value));
    }

    /// <summary>
    /// Shows what this agent would retrieve for a question.
    /// </summary>
    /// <remarks>
    /// It reads and changes nothing, so it takes no precondition and returns no
    /// entity tag. It does cost one embedding call when the agent has ready
    /// knowledge, and nothing at all when it has none.
    /// </remarks>
    private static async Task<IResult> SearchKnowledgeAsync(
        Guid tenantId,
        Guid aiAgentId,
        KnowledgeSearchRequest request,
        SearchAgentKnowledgeHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        KnowledgeSearchResult? result = await handler
            .HandleAsync(
                new SearchAgentKnowledgeQuery(tenantId, aiAgentId, request.Question),
                cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? AgentNotFound(aiAgentId)
            : Results.Ok(KnowledgeSearchResponse.From(result));
    }

    private static IResult Respond(HttpContext http, Versioned<AiAgentView>? agent, Guid aiAgentId)
    {
        if (agent is null)
        {
            return AgentNotFound(aiAgentId);
        }

        SetETag(http, agent.Version);

        return Results.Ok(AiAgentResponse.From(agent.Value));
    }

    /// <summary>
    /// Publishes the resource version as a strong entity tag.
    /// </summary>
    /// <remarks>
    /// A header, never the body. The JSON representation stays the accepted seven
    /// fields with no version, row version, transaction id or other persistence
    /// metadata in it -- which is also why the version is carried beside the view
    /// through the Application rather than inside it.
    /// </remarks>
    private static void SetETag(HttpContext http, ConcurrencyToken version) =>
        http.Response.Headers.ETag = EntityTags.Format(version);

    /// <summary>
    /// The single not-found response for every agent operation.
    /// </summary>
    /// <remarks>
    /// Identical whether the agent does not exist at all or exists under another
    /// tenant, and it says nothing about which. The wording is chosen with that in
    /// mind: it reports what this tenant has and makes no claim about the wider
    /// system, so it stays accurate without ever confirming that an id is real
    /// somewhere else.
    /// <para>
    /// It is also what a cross-tenant request carrying the target's genuine
    /// entity tag receives. The lookup is tenant-scoped and runs before any
    /// version is compared, so a valid tag never becomes an alternative route to
    /// somebody else's agent -- and never produces a <c>412</c> that would confirm
    /// the agent exists.
    /// </para>
    /// <para>
    /// <c>Results.Problem</c>, not <c>Results.NotFound</c>: the API answers every
    /// error in one contract, and an empty 404 body would be the exception.
    /// </para>
    /// </remarks>
    private static IResult AgentNotFound(Guid aiAgentId) =>
        Results.Problem(
            title: "AI agent not found",
            detail: $"This tenant has no AI agent with id '{aiAgentId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
