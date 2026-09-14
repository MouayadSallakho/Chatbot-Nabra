using Microsoft.AspNetCore.Mvc;
using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Files;

namespace OmniAI.Api.Knowledge;

/// <summary>
/// Uploading business files into Knowledge.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Multipart, and only for this.</strong> Every other route on this
/// platform speaks JSON; a file base64-encoded inside a JSON body would be a
/// third larger on the wire and would have to be decoded into a second copy
/// before anything could read it. These two routes take the raw parts.
/// </para>
/// <para>
/// <strong>The body is bounded before it is read.</strong> A request larger than
/// the policy allows is refused by the server without being buffered, so an
/// oversized upload costs a connection rather than the memory it claimed.
/// </para>
/// <para>
/// <strong>Nothing here embeds.</strong> An upload extracts text and stops; the
/// document arrives needing an index, which somebody asks for deliberately. That
/// is what stops uploading a folder of PDFs from quietly spending a provider
/// budget.
/// </para>
/// <para>
/// Authenticated and antiforgery-checked like every other knowledge route.
/// A multipart POST is exactly the shape a cross-site form submission takes, so
/// this is the last place to make an exception.
/// </para>
/// </remarks>
internal static class KnowledgeFileEndpoints
{
    /// <summary>The form field carrying the file.</summary>
    private const string FileField = "file";

    /// <summary>The form field carrying an optional title.</summary>
    private const string TitleField = "title";

    public static IEndpointRouteBuilder MapKnowledgeFileEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        KnowledgeFilePolicy policy =
            endpoints.ServiceProvider.GetRequiredService<KnowledgeFilePolicy>();

        RouteGroupBuilder knowledge = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/knowledge")
            .WithTags("Knowledge")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // The group's antiforgery filter still applies: a multipart POST is
        // exactly the shape a cross-site form submission takes, so this is the
        // last place to make an exception.
        knowledge.MapPost("/files", UploadAsync)
            .WithName("UploadKnowledgeFile")
            .WithMetadata(new RequestSizeLimitAttribute(policy.MaxFileBytes));

        knowledge.MapPut("/{knowledgeDocumentId:guid}/source", ReplaceAsync)
            .WithName("ReplaceKnowledgeSource")
            .WithMetadata(new RequestSizeLimitAttribute(policy.MaxFileBytes));

        return endpoints;
    }

    /// <summary><c>POST /api/tenants/{tenantId}/knowledge/files</c>.</summary>
    private static async Task<IResult> UploadAsync(
        HttpContext http,
        Guid tenantId,
        UploadKnowledgeFileHandler handler,
        KnowledgeFilePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handler);

        (KnowledgeFileUpload? upload, string? title, IResult? failure) =
            await ReadFormAsync(http, policy, cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            return failure;
        }

        Versioned<KnowledgeFileResult> created = await handler
            .HandleAsync(
                new UploadKnowledgeFileCommand(tenantId, title, upload!),
                cancellationToken)
            .ConfigureAwait(false);

        http.Response.Headers.ETag = EntityTags.Format(created.Version);

        return Results.Created(
            $"/api/tenants/{tenantId}/knowledge/{created.Value.Document.Id}",
            KnowledgeDocumentResponse.From(created.Value.Document));
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/knowledge/{knowledgeDocumentId}/source</c>.
    /// </summary>
    private static async Task<IResult> ReplaceAsync(
        HttpContext http,
        Guid tenantId,
        Guid knowledgeDocumentId,
        ReplaceKnowledgeSourceHandler handler,
        KnowledgeFilePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handler);

        // Before the body is read. Somebody who has not said which version they
        // are replacing is told so without this deployment first buffering ten
        // megabytes on their behalf.
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult missing)
        {
            return missing;
        }

        (KnowledgeFileUpload? upload, _, IResult? failure) =
            await ReadFormAsync(http, policy, cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            return failure;
        }

        Versioned<KnowledgeFileResult>? replaced = await handler
            .HandleAsync(
                new ReplaceKnowledgeSourceCommand(
                    tenantId,
                    knowledgeDocumentId,
                    upload!,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        if (replaced is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(replaced.Version);

        return Results.Ok(KnowledgeDocumentResponse.From(replaced.Value.Document));
    }

    /// <summary>
    /// Reads the file and the optional title out of a multipart body.
    /// </summary>
    /// <remarks>
    /// <strong>The size is checked twice, and both are needed.</strong> The
    /// server's own limit refuses a body too large to buffer; this one refuses a
    /// file too large to accept, which is a smaller number and produces the
    /// message a person can act on. Between them there is no path by which an
    /// unbounded amount of somebody else's bytes reaches a parser.
    /// </remarks>
    private static async Task<(KnowledgeFileUpload? Upload, string? Title, IResult? Failure)>
        ReadFormAsync(HttpContext http, KnowledgeFilePolicy policy, CancellationToken cancellationToken)
    {
        if (!http.Request.HasFormContentType)
        {
            return (null, null, Results.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "A file upload is required",
                detail: "Send the file as multipart/form-data with a \"file\" part."));
        }

        IFormCollection form;

        try
        {
            form = await http.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException oversized)
            when (oversized.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, null, TooLarge(policy));
        }
        catch (InvalidDataException)
        {
            // The server's multipart reader refusing a body past its bound.
            return (null, null, TooLarge(policy));
        }

        IFormFile? file = form.Files.GetFile(FileField);

        if (file is null || file.Length == 0)
        {
            return (null, null, Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A file is required",
                detail: "Choose a file to upload."));
        }

        if (file.Length > policy.MaxFileBytes)
        {
            return (null, null, TooLarge(policy));
        }

        // Into memory, once, and never onto disk. The array lives as long as this
        // request and no longer: there is no upload folder and nothing to clean
        // up afterwards.
        using MemoryStream buffer = new((int)file.Length);

        await using (Stream content = file.OpenReadStream())
        {
            await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        string? title = form.TryGetValue(TitleField, out Microsoft.Extensions.Primitives.StringValues values)
            ? values.ToString()
            : null;

        return (
            new KnowledgeFileUpload(file.FileName, file.ContentType, buffer.ToArray()),
            string.IsNullOrWhiteSpace(title) ? null : title,
            null);
    }

    private static IResult TooLarge(KnowledgeFilePolicy policy) =>
        Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "That file is too large",
            detail: $"Uploads must be {policy.MaxFileMegabytes} MB or smaller. Split the file "
                + "into smaller documents, or upload the part that matters.");
}
