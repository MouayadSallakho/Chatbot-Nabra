using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;

namespace OmniAI.Api.Notifications;

/// <summary>
/// The signed-in member's notifications and operational-email preferences.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every route is "mine".</strong> None takes a user id. There is no way
/// to address another member's notifications or preferences, so there is nothing
/// to authorise for one — administrators included.
/// </para>
/// <para>
/// <strong>Absent rather than forbidden.</strong> A non-member gets 404 for the
/// workspace, and a notification id that is not the caller's gets 404 for the
/// notification, exactly like an id that never existed.
/// </para>
/// </remarks>
internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder notifications = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/notifications")
            .WithTags("Notifications")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        notifications.MapGet("/", ListAsync).WithName("ListNotifications");
        notifications.MapGet("/unread-count", UnreadCountAsync).WithName("GetUnreadNotificationCount");
        notifications.MapPost("/{notificationId:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        notifications.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");
        notifications.MapGet("/preferences", PreferencesAsync).WithName("GetNotificationPreferences");
        notifications.MapPut("/preferences", SavePreferencesAsync).WithName("SaveNotificationPreferences");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        HttpContext http,
        NotificationHandlers handlers,
        CancellationToken cancellationToken)
    {
        NotificationPage? result = await handlers.ListAsync(tenantId, page, pageSize, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return NoSuchWorkspace();
        }

        NoStore(http);

        return Results.Ok(NotificationPageResponse.From(result));
    }

    private static async Task<IResult> UnreadCountAsync(
        Guid tenantId,
        HttpContext http,
        NotificationHandlers handlers,
        CancellationToken cancellationToken)
    {
        int? unread = await handlers.UnreadCountAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (unread is not int count)
        {
            return NoSuchWorkspace();
        }

        NoStore(http);

        return Results.Ok(new UnreadCountResponse(count));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid tenantId,
        Guid notificationId,
        NotificationHandlers handlers,
        CancellationToken cancellationToken)
    {
        bool? found = await handlers.MarkReadAsync(tenantId, notificationId, cancellationToken).ConfigureAwait(false);

        return found switch
        {
            null => NoSuchWorkspace(),
            false => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such notification",
                detail: "This notification does not exist, or it is not yours."),
            true => Results.NoContent(),
        };
    }

    private static async Task<IResult> MarkAllReadAsync(
        Guid tenantId,
        NotificationHandlers handlers,
        CancellationToken cancellationToken)
    {
        int? marked = await handlers.MarkAllReadAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return marked is int count ? Results.Ok(new MarkAllReadResponse(count)) : NoSuchWorkspace();
    }

    private static async Task<IResult> PreferencesAsync(
        Guid tenantId,
        HttpContext http,
        NotificationHandlers handlers,
        CancellationToken cancellationToken) =>
        Respond(http, await handlers.PreferencesAsync(tenantId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SavePreferencesAsync(
        Guid tenantId,
        SaveNotificationPreferencesRequest? request,
        HttpContext http,
        NotificationHandlers handlers,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult refusal)
        {
            return refusal;
        }

        try
        {
            return Respond(
                http,
                await handlers
                    .SavePreferencesAsync(
                        new SaveNotificationPreferencesCommand(
                            tenantId,
                            request?.Email?.Reports,
                            request?.Email?.Usage,
                            request?.Email?.Billing,
                            request?.Email?.Knowledge,
                            request?.Email?.ChannelHealth,
                            expected),
                        cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (NotificationPreferencesVersionMismatchException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Your preferences changed",
                detail: failure.Message);
        }
    }

    private static IResult Respond(HttpContext http, Versioned<NotificationPreferencesView>? preferences)
    {
        if (preferences is null)
        {
            return NoSuchWorkspace();
        }

        NoStore(http);
        http.Response.Headers.ETag = EntityTags.Format(preferences.Version);

        return Results.Ok(NotificationPreferencesResponse.From(preferences.Value));
    }

    /// <summary>Personal and changing: never cached by a browser or a proxy.</summary>
    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";

    private static IResult NoSuchWorkspace() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No such workspace",
            detail: "This workspace does not exist, or you are not a member of it.");
}

/// <summary>One notification on the wire: server-written text and an internal route, nothing else.</summary>
public sealed record NotificationResponse(
    Guid Id,
    string Kind,
    string Category,
    string Severity,
    string Title,
    string Body,
    string? ActionPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    public static NotificationResponse From(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationResponse(
            notification.Id.Value,
            notification.Kind.ToString(),
            notification.Category.ToString(),
            notification.Severity.ToString(),
            notification.Title,
            notification.Body,
            notification.ActionPath,
            notification.CreatedAt,
            notification.ReadAt);
    }
}

public sealed record NotificationPageResponse(
    IReadOnlyList<NotificationResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount)
{
    public static NotificationPageResponse From(NotificationPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new NotificationPageResponse(
            [.. page.Items.Select(NotificationResponse.From)],
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.UnreadCount);
    }
}

public sealed record UnreadCountResponse(int UnreadCount);

public sealed record MarkAllReadResponse(int Marked);

/// <summary>Which categories are emailed. In-app notifications are not a preference.</summary>
public sealed record EmailPreferencesContract(
    bool? Reports,
    bool? Usage,
    bool? Billing,
    bool? Knowledge,
    bool? ChannelHealth);

public sealed record SaveNotificationPreferencesRequest(EmailPreferencesContract? Email);

/// <param name="EmailConfigured">False when this deployment cannot send email at all.</param>
public sealed record NotificationPreferencesResponse(
    EmailPreferencesContract Email,
    bool EmailConfigured,
    DateTimeOffset UpdatedAt)
{
    public static NotificationPreferencesResponse From(NotificationPreferencesView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        NotificationPreferences preferences = view.Preferences;

        return new NotificationPreferencesResponse(
            new EmailPreferencesContract(
                preferences.EmailReports,
                preferences.EmailUsage,
                preferences.EmailBilling,
                preferences.EmailKnowledge,
                preferences.EmailChannelHealth),
            view.EmailConfigured,
            preferences.UpdatedAt);
    }
}
