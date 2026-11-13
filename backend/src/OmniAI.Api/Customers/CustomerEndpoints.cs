using OmniAI.Api.ErrorHandling;
using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Customers;
using OmniAI.Application.Customers.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;

namespace OmniAI.Api.Customers;

/// <summary>
/// Reading and administering a workspace's customer directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two permissions, and a third borrowed.</strong> Reading needs
/// <c>customers.view</c>, writing needs <c>customers.manage</c>, and a customer's
/// conversation history appears only for somebody who also holds
/// <c>conversations.view</c>. That last one is the point of the design: a
/// directory must not become a way around the permission that guards what
/// customers actually said.
/// </para>
/// <para>
/// <strong>No response here carries a raw channel identifier.</strong> A WhatsApp
/// number leaves as its last four digits, masked by the Domain value object
/// before it reaches this layer. There is no unmasked variant and no query
/// parameter that produces one.
/// </para>
/// <para>
/// <strong>There is no delete.</strong> Nothing in this phase removes a customer
/// or their history, and a merge tombstones rather than deletes, so an old link
/// still resolves to an answer.
/// </para>
/// </remarks>
internal static class CustomerEndpoints
{
    private const string GetCustomerRouteName = "GetCustomer";

    // Phase 38.7b (BA-4): the codes only this edge can name. The rest are named at
    // their throw sites; all of them are in problem-codes.md.

    /// <summary><c>page</c> or <c>pageSize</c> out of range (400).</summary>
    internal const string PageInvalid = "customers.page.invalid";

    /// <summary>A merge that names no customer to keep (400).</summary>
    internal const string MergeTargetRequired = "customers.merge.target_required";

    /// <summary>The customer was merged into another, which is the one to use (409).</summary>
    internal const string Merged = "customers.merged";

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder customers = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/customers")
            .WithTags("Customers")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // Phase 38.7a: the two reads declare their bodies, so the document shows
        // labelCode and shortCode. Metadata only.
        customers.MapGet("/", ListAsync)
            .WithName("ListCustomers")
            .WithSummary("Lists the workspace's canonical customers, each with the English label, its labelCode and shortCode (the conversation reads' own).")
            .Produces<CustomerPageResponse>(StatusCodes.Status200OK);

        customers.MapGet("/{customerId:guid}", GetAsync)
            .WithName(GetCustomerRouteName)
            .WithSummary("Reads one customer with its ETag, label, labelCode and shortCode; a merged record names its canonical one.")
            .Produces<CustomerResponse>(StatusCodes.Status200OK);

        // PUT to a named sub-resource rather than a general PATCH on the
        // customer. The label is the only editable field there is, and a generic
        // JSON patch would be a hole somebody widens later without noticing.
        customers.MapPut("/{customerId:guid}/display-name", RenameAsync)
            .WithName("RenameCustomer")
            .Produces<CustomerResponse>(StatusCodes.Status200OK);

        // POST to a named action, because combining two people's histories is a
        // decision rather than a property edit, and naming it keeps the audit
        // readable.
        customers.MapPost("/{customerId:guid}/merge", MergeAsync)
            .WithName("MergeCustomers")
            .Produces<CustomerMergeResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary><c>GET /api/tenants/{tenantId}/customers</c>.</summary>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        CustomerHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        try
        {
            CustomerPage? result = await handlers
                .ListAsync(new ListCustomersQuery(tenantId, page, pageSize), cancellationToken)
                .ConfigureAwait(false);

            return result is null
                ? Results.NotFound()
                : Results.Ok(CustomerPageResponse.From(result));
        }
        catch (CustomerPageValidationException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "That page cannot be served",
                detail: failure.Message,
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = PageInvalid });
        }
    }

    /// <summary><c>GET /api/tenants/{tenantId}/customers/{customerId}</c>.</summary>
    private static async Task<IResult> GetAsync(
        Guid tenantId,
        Guid customerId,
        HttpContext http,
        CustomerHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        Versioned<CustomerView>? customer = await handlers
            .GetAsync(new GetCustomerQuery(tenantId, customerId), cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, customer);
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/customers/{customerId}/display-name</c>.
    /// </summary>
    private static async Task<IResult> RenameAsync(
        Guid tenantId,
        Guid customerId,
        RenameCustomerRequest? request,
        HttpContext http,
        CustomerHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult refusal)
        {
            return refusal;
        }

        try
        {
            Versioned<CustomerView>? customer = await handlers
                .RenameAsync(
                    new RenameCustomerCommand(
                        tenantId,
                        customerId,
                        request?.DisplayName,
                        expected),
                    cancellationToken)
                .ConfigureAwait(false);

            return Respond(http, customer);
        }
        catch (CustomerVersionMismatchException failure)
        {
            return Stale(failure.Message);
        }
        catch (CustomerAlreadyMergedException failure)
        {
            return MergedAway(failure);
        }
    }

    /// <summary><c>POST /api/tenants/{tenantId}/customers/{customerId}/merge</c>.</summary>
    private static async Task<IResult> MergeAsync(
        Guid tenantId,
        Guid customerId,
        MergeCustomerRequest? request,
        HttpContext http,
        CustomerHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (request?.TargetCustomerId is not Guid targetId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A customer to keep is required",
                detail: "Combining two customers needs the record that will survive.",
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = MergeTargetRequired });
        }

        // The route customer is the one being merged away, and the version
        // guarded is that record's. It is the one that changes irreversibly, so a
        // request built against a stale view of it must not go through.
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult refusal)
        {
            return refusal;
        }

        try
        {
            CustomerMergeResult? result = await handlers
                .MergeAsync(
                    new MergeCustomersCommand(tenantId, customerId, targetId, expected),
                    cancellationToken)
                .ConfigureAwait(false);

            return result is null
                ? Results.NotFound()
                : Results.Ok(CustomerMergeResponse.From(result));
        }
        catch (CustomerVersionMismatchException failure)
        {
            return Stale(failure.Message);
        }
        catch (CustomerAlreadyMergedException failure)
        {
            return MergedAway(failure);
        }
        catch (CustomerMergeNotAllowedException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "These customers cannot be combined",
                detail: failure.Message,
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = failure.Code });
        }
    }

    private static IResult Respond(HttpContext http, Versioned<CustomerView>? customer)
    {
        if (customer is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(customer.Version);

        return Results.Ok(CustomerResponse.From(customer.Value));
    }

    private static IResult Stale(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "This customer changed",
            detail: detail);

    /// <summary>
    /// The answer for an operation aimed at a record that has been merged away.
    /// </summary>
    /// <remarks>
    /// 409 with the canonical id in the body, rather than a 404 or a redirect. The
    /// caller's next move is always to act on the surviving record, and making
    /// them go and find it is how a stale tab becomes a dead end.
    /// </remarks>
    private static IResult MergedAway(CustomerAlreadyMergedException failure) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This customer has been combined with another",
            detail: "It was combined into another customer, which is the record to use.",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ProblemCodes.Member] = Merged,
                ["customerId"] = failure.CustomerId.Value,
                ["canonicalCustomerId"] = failure.CanonicalId.Value,
            });
}

/// <summary>Set or clear a customer's label.</summary>
/// <param name="DisplayName">
/// The new label, or <see langword="null"/>/blank to remove one.
/// </param>
internal sealed record RenameCustomerRequest(string? DisplayName);

/// <summary>Combine this customer into another.</summary>
internal sealed record MergeCustomerRequest(Guid? TargetCustomerId);

/// <summary>One directory row.</summary>
/// <param name="Label">The English label, kept for older clients and exports.</param>
/// <param name="LabelCode">
/// <c>Named</c>, <c>WhatsAppCustomer</c>, <c>AnonymousVisitor</c> or
/// <c>UnnamedCustomer</c>, by the rule the conversation reads use (Phase 38.7a):
/// the client words the label by this code, in the reader's language.
/// </param>
/// <param name="ShortCode">
/// Six Crockford base32 characters derived from the customer id, equal to the
/// conversation reads' (Phase 38.7a). A label, never a key.
/// </param>
internal sealed record CustomerRowResponse(
    Guid Id,
    string? DisplayName,
    string Label,
    IReadOnlyList<string> Channels,
    int ConversationCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    string LabelCode,
    string ShortCode)
{
    public static CustomerRowResponse From(CustomerRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new CustomerRowResponse(
            row.Id,
            row.DisplayName,
            row.Label,
            [.. row.Channels.Select(channel => channel.ToString())],
            row.ConversationCount,
            row.LastActivityAt,
            row.CreatedAt,
            row.LabelCode.ToString(),
            row.ShortCode);
    }
}

/// <summary>One page of the directory.</summary>
internal sealed record CustomerPageResponse(
    IReadOnlyList<CustomerRowResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static CustomerPageResponse From(CustomerPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new CustomerPageResponse(
            [.. page.Items.Select(CustomerRowResponse.From)],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

/// <summary>One channel identity, already masked.</summary>
internal sealed record CustomerIdentityResponse(string Channel, string Reference)
{
    public static CustomerIdentityResponse From(CustomerChannelIdentityView identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return new CustomerIdentityResponse(identity.Channel.ToString(), identity.Reference);
    }
}

/// <summary>One conversation of a customer's. Metadata only.</summary>
internal sealed record CustomerConversationResponse(
    Guid Id,
    string Channel,
    string Status,
    string Handling,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt)
{
    public static CustomerConversationResponse From(CustomerConversationView conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        return new CustomerConversationResponse(
            conversation.ConversationId,
            conversation.Channel.ToString(),
            conversation.Status.ToString(),
            conversation.Handling.ToString(),
            conversation.StartedAt,
            conversation.LastActivityAt);
    }
}

/// <summary>One customer, in full.</summary>
/// <param name="LabelCode">As on the directory row (Phase 38.7a).</param>
/// <param name="ShortCode">As on the directory row (Phase 38.7a).</param>
internal sealed record CustomerResponse(
    Guid Id,
    string? DisplayName,
    string Label,
    IReadOnlyList<CustomerIdentityResponse> Identities,
    IReadOnlyList<string> Channels,
    int ConversationCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? MergedIntoCustomerId,
    DateTimeOffset? MergedAt,
    IReadOnlyList<CustomerConversationResponse> Conversations,
    bool CanReadConversations,
    string LabelCode,
    string ShortCode)
{
    public static CustomerResponse From(CustomerView customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        return new CustomerResponse(
            customer.Id,
            customer.DisplayName,
            customer.Label,
            [.. customer.Identities.Select(CustomerIdentityResponse.From)],
            [.. customer.Channels.Select(channel => channel.ToString())],
            customer.ConversationCount,
            customer.LastActivityAt,
            customer.CreatedAt,
            customer.UpdatedAt,
            customer.MergedIntoCustomerId,
            customer.MergedAt,
            [.. customer.Conversations.Select(CustomerConversationResponse.From)],
            customer.CanReadConversations,
            customer.LabelCode.ToString(),
            customer.ShortCode);
    }
}

/// <summary>What a merge did.</summary>
internal sealed record CustomerMergeResponse(
    Guid SourceCustomerId,
    Guid TargetCustomerId,
    int ConversationsMoved,
    int IdentitiesMoved,
    bool AlreadyMerged)
{
    public static CustomerMergeResponse From(CustomerMergeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CustomerMergeResponse(
            result.SourceCustomerId,
            result.TargetCustomerId,
            result.ConversationsMoved,
            result.IdentitiesMoved,
            result.AlreadyMerged);
    }
}
