using OmniAI.Api.Security;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;

namespace OmniAI.Api.Conversations;

/// <summary>One conversation, in a workspace's list.</summary>
public sealed record ConversationSummaryResponse(
    Guid Id,
    string Channel,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    string? AgentName,
    int MessageCount,
    string? LastMessagePreview,
    string? LastMessageSender);

/// <summary>A page of conversations.</summary>
public sealed record ConversationPageResponse(
    IReadOnlyList<ConversationSummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>One conversation's detail.</summary>
public sealed record ConversationDetailResponse(
    Guid Id,
    string Channel,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    string? AgentName,
    int MessageCount);

/// <summary>One message in a transcript.</summary>
/// <remarks>
/// Carries the ingestion sequence because it is the pagination cursor. It is not
/// a timestamp and must not be rendered as one; it is the platform's ordering,
/// which is what makes paging a transcript stable while it is still being added
/// to.
/// </remarks>
public sealed record TranscriptMessageResponse(
    Guid Id,
    string Sender,
    string Text,
    DateTimeOffset SentAt,
    long Sequence);

/// <summary>A page of a transcript.</summary>
public sealed record TranscriptPageResponse(
    IReadOnlyList<TranscriptMessageResponse> Items,
    long? NextCursor);

/// <summary>
/// Reading a workspace's conversations.
/// </summary>
/// <remarks>
/// <strong>Read-only, and every route needs <c>conversations.view</c>.</strong>
/// There is no reply, no assignment and no delete, because none of those exist
/// yet — and an endpoint that accepted a reply would need a human-agent identity
/// the platform has not designed.
/// <para>
/// <strong>An anonymous visitor's credential is worth nothing here.</strong>
/// These routes require an authenticated session and a membership decision; the
/// conversation cookie is not read, is not sent to this path, and could not
/// satisfy the authorizer if it were.
/// </para>
/// </remarks>
internal static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workspace = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/conversations")
            .WithTags("Conversations")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        workspace.MapGet("/", ListAsync)
            .WithName("ListTenantConversations");

        workspace.MapGet("/{conversationId:guid}", GetAsync)
            .WithName("GetTenantConversationById");

        workspace.MapGet("/{conversationId:guid}/messages", ListMessagesAsync)
            .WithName("ListTenantConversationMessages");

        return endpoints;
    }

    /// <summary><c>GET /api/tenants/{tenantId}/conversations</c>.</summary>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        ReadConversationsHandler handler,
        CancellationToken cancellationToken)
    {
        ConversationPage? result = await handler
            .ListAsync(new ListConversationsQuery(tenantId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new ConversationPageResponse(
                [.. result.Items.Select(item => new ConversationSummaryResponse(
                    item.Id,
                    item.Channel.ToString(),
                    item.Status.ToString(),
                    item.StartedAt,
                    item.LastActivityAt,
                    item.AiAgentName,
                    item.MessageCount,
                    item.LastMessagePreview,
                    item.LastMessageSender?.ToString()))],
                result.Page,
                result.PageSize,
                result.Total));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/conversations/{conversationId}</c>.</summary>
    private static async Task<IResult> GetAsync(
        Guid tenantId,
        Guid conversationId,
        ReadConversationsHandler handler,
        CancellationToken cancellationToken)
    {
        ConversationDetail? detail = await handler
            .GetAsync(new GetConversationQuery(tenantId, conversationId), cancellationToken)
            .ConfigureAwait(false);

        return detail is null
            ? Results.NotFound()
            : Results.Ok(
                new ConversationDetailResponse(
                    detail.Id,
                    detail.Channel.ToString(),
                    detail.Status.ToString(),
                    detail.StartedAt,
                    detail.LastActivityAt,
                    detail.AiAgentName,
                    detail.MessageCount));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/conversations/{conversationId}/messages</c>.</summary>
    /// <remarks>
    /// Paged by ingestion sequence, never by time. A cursor of "after sequence N"
    /// stays correct while the conversation is still being added to, which a page
    /// number would not: a customer replying between two page reads would shift
    /// every subsequent offset.
    /// </remarks>
    private static async Task<IResult> ListMessagesAsync(
        Guid tenantId,
        Guid conversationId,
        long? after,
        int? limit,
        ReadConversationsHandler handler,
        CancellationToken cancellationToken)
    {
        ConversationMessagePage? page = await handler
            .ListMessagesAsync(
                new ListConversationMessagesQuery(tenantId, conversationId, after, limit),
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new TranscriptPageResponse(
                [.. page.Items.Select(message => new TranscriptMessageResponse(
                    message.Id,
                    message.Sender.ToString(),
                    message.Text,
                    message.SentAt,
                    message.IngestionSequence))],
                page.NextCursor));
    }
}
