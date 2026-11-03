using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.CreateKnowledgeDocument;
using OmniAI.Application.Knowledge.GetKnowledgeDocumentById;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.SetKnowledgeDocumentStatus;
using OmniAI.Application.Knowledge.UpdateKnowledgeDocument;

namespace OmniAI.Api.Knowledge;

/// <summary>
/// The knowledge document endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Authenticated and antiforgery-checked as a group, exactly like every other
/// admin surface. Nothing here is reachable by the public: a customer talking to
/// an agent may be answered <em>from</em> knowledge, and can never read, list or
/// search it.
/// </para>
/// <para>
/// <strong>Every mutation requires <c>If-Match</c>, indexing included.</strong>
/// Indexing is a write: it replaces the chunk set and moves the document's
/// version. Allowing it unconditionally would let somebody index a draft that had
/// been replaced while their screen was open, and the resulting index would
/// describe text nobody could see.
/// </para>
/// </remarks>
internal static class KnowledgeEndpoints
{
    private const string GetKnowledgeDocumentByIdRouteName = "GetKnowledgeDocumentById";

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder knowledge = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/knowledge")
            .WithTags("Knowledge")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        knowledge.MapGet("/", ListAsync)
            .WithName("ListKnowledgeDocuments")
            .WithSummary("One page of the workspace's knowledge documents, each with its file size and last indexing outcome.")
            .Produces<KnowledgeDocumentPageResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        knowledge.MapPost("/", CreateAsync)
            .WithName("CreateKnowledgeDocument");

        knowledge.MapGet("/{knowledgeDocumentId:guid}", GetByIdAsync)
            .WithName(GetKnowledgeDocumentByIdRouteName)
            .WithSummary("One knowledge document with its ETag and last indexing outcome.")
            .Produces<KnowledgeDocumentDetailResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        knowledge.MapPut("/{knowledgeDocumentId:guid}", UpdateAsync)
            .WithName("UpdateKnowledgeDocument");

        knowledge.MapPost("/{knowledgeDocumentId:guid}/index", IndexAsync)
            .WithName("IndexKnowledgeDocument");

        knowledge.MapPost("/{knowledgeDocumentId:guid}/enable", EnableAsync)
            .WithName("EnableKnowledgeDocument");

        knowledge.MapPost("/{knowledgeDocumentId:guid}/disable", DisableAsync)
            .WithName("DisableKnowledgeDocument");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        ListKnowledgeDocumentsHandler handler,
        CancellationToken cancellationToken)
    {
        KnowledgeDocumentPage documents = await handler
            .HandleAsync(new ListKnowledgeDocumentsQuery(tenantId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(KnowledgeDocumentPageResponse.From(documents));
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http,
        Guid tenantId,
        KnowledgeDocumentRequest request,
        CreateKnowledgeDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Versioned<KnowledgeDocumentView> created = await handler
            .HandleAsync(
                new CreateKnowledgeDocumentCommand(tenantId, request.Title, request.Content),
                cancellationToken)
            .ConfigureAwait(false);

        SetETag(http, created.Version);

        return Results.CreatedAtRoute(
            GetKnowledgeDocumentByIdRouteName,
            new
            {
                tenantId = created.Value.TenantId,
                knowledgeDocumentId = created.Value.Id,
            },
            KnowledgeDocumentResponse.From(created.Value));
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        GetKnowledgeDocumentByIdHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<KnowledgeDocumentView>? document = await handler
            .HandleAsync(
                new GetKnowledgeDocumentByIdQuery(tenantId, knowledgeDocumentId),
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return NotFound(knowledgeDocumentId);
        }

        // The read alone carries the last indexing outcome (Phase 37.5a); the
        // writes keep their shape. See KnowledgeDocumentDetailResponse.
        SetETag(http, document.Version);

        return Results.Ok(KnowledgeDocumentDetailResponse.From(document.Value));
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        KnowledgeDocumentRequest request,
        UpdateKnowledgeDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<KnowledgeDocumentView>? updated = await handler
            .HandleAsync(
                new UpdateKnowledgeDocumentCommand(
                    tenantId,
                    knowledgeDocumentId,
                    request.Title,
                    request.Content,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated, knowledgeDocumentId);
    }

    private static async Task<IResult> IndexAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        IndexKnowledgeDocumentHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<KnowledgeIndexResult>? indexed = await handler
            .HandleAsync(
                new IndexKnowledgeDocumentCommand(tenantId, knowledgeDocumentId, expected),
                cancellationToken)
            .ConfigureAwait(false);

        if (indexed is null)
        {
            return NotFound(knowledgeDocumentId);
        }

        SetETag(http, indexed.Version);

        return Results.Ok(KnowledgeIndexResponse.From(indexed.Value));
    }

    private static Task<IResult> EnableAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        SetKnowledgeDocumentStatusHandler handler,
        CancellationToken cancellationToken) =>
        SetStatusAsync(http, tenantId, knowledgeDocumentId, true, handler, cancellationToken);

    private static Task<IResult> DisableAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        SetKnowledgeDocumentStatusHandler handler,
        CancellationToken cancellationToken) =>
        SetStatusAsync(http, tenantId, knowledgeDocumentId, false, handler, cancellationToken);

    private static async Task<IResult> SetStatusAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        bool enabled,
        SetKnowledgeDocumentStatusHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<KnowledgeDocumentView>? changed = await handler
            .HandleAsync(
                new SetKnowledgeDocumentStatusCommand(
                    tenantId,
                    knowledgeDocumentId,
                    enabled,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, changed, knowledgeDocumentId);
    }

    private static IResult Respond(
        HttpContext http,
        Versioned<KnowledgeDocumentView>? document,
        Guid knowledgeDocumentId)
    {
        if (document is null)
        {
            return NotFound(knowledgeDocumentId);
        }

        SetETag(http, document.Version);

        return Results.Ok(KnowledgeDocumentResponse.From(document.Value));
    }

    private static void SetETag(HttpContext http, ConcurrencyToken version) =>
        http.Response.Headers.ETag = EntityTags.Format(version);

    private static IResult NotFound(Guid knowledgeDocumentId) =>
        Results.Problem(
            title: "Knowledge document not found",
            detail: $"This workspace has no knowledge document with id '{knowledgeDocumentId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
