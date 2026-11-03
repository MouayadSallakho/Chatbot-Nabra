using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.GetWebChatWidget;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.UpdateWebChatWidget;

public sealed record UpdateWebChatWidgetCommand(
    Guid TenantId,
    Guid WebChatEndpointId,
    string? Greeting,
    string? AccentColor,
    string? LauncherPosition,
    string? Theme,
    IReadOnlyList<string>? AllowedOrigins,
    ConcurrencyToken Expected);

/// <summary>
/// Replaces a widget's appearance and the websites it may be embedded on.
/// </summary>
/// <remarks>
/// <para>
/// Everything is validated before the workspace is consulted, so a malformed
/// origin is refused identically whether the endpoint is reachable or not -- and
/// the reader is told which line is wrong rather than being told the whole save
/// failed.
/// </para>
/// <para>
/// <strong>Removing a website takes effect on the next embedded request.</strong>
/// The allowlist is re-checked on every embedded read and send, so a widget
/// already open on a site that has just been removed stops working without
/// anybody having to hunt down its session. The conversation itself survives and
/// stays visible in the admin, because the customer really did say those things.
/// </para>
/// </remarks>
public sealed class UpdateWebChatWidgetHandler(
    IWebChatEndpointRepository endpoints,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<WebChatWidgetConfigurationView>?> HandleAsync(
        UpdateWebChatWidgetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        WebChatEndpointId endpointId = WebChatEndpointId.From(command.WebChatEndpointId);

        WebChatWidgetSettings widget = ReadWidget(command);
        IReadOnlyList<WebChatOrigin> origins = ReadOrigins(command.AllowedOrigins);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WebsiteChatManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<WebChatEndpoint>? stored = await _endpoints
            .FindAsync(tenantId, endpointId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        if (stored.Version != command.Expected)
        {
            throw new WebChatEndpointVersionMismatchException(command.Expected);
        }

        stored.Value.Configure(widget, origins, _clock.GetUtcNow());

        ConcurrencyToken version = await _endpoints
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<WebChatWidgetConfigurationView>(
            WebChatWidgetConfiguration.From(stored.Value),
            version);
    }

    private static WebChatWidgetSettings ReadWidget(UpdateWebChatWidgetCommand command) =>
        WebChatWidgetSettings.Create(
            WebChatGreeting.CreateOrNone(command.Greeting),
            string.IsNullOrWhiteSpace(command.AccentColor)
                ? WebChatAccentColor.Default
                : WebChatAccentColor.Create(command.AccentColor),
            Read<WebChatLauncherPosition>(
                command.LauncherPosition,
                "launcher position",
                "website-chat.launcher_position.invalid"),
            Read<WebChatTheme>(command.Theme, "widget theme", "website-chat.theme.invalid"));

    /// <summary>
    /// Reads the intended list of websites, reporting which line is wrong.
    /// </summary>
    /// <remarks>
    /// The origin's own message names the problem -- a path, a wildcard, plain
    /// HTTP -- and quoting the offending line back is what lets somebody fix a
    /// typo in a list of twenty without hunting for it.
    /// </remarks>
    private static List<WebChatOrigin> ReadOrigins(IReadOnlyList<string>? supplied)
    {
        if (supplied is null || supplied.Count == 0)
        {
            return [];
        }

        if (supplied.Count > WebChatEndpoint.MaxAllowedOrigins)
        {
            throw new DomainValidationException(
                $"A website chat widget may be allowed on at most "
                + $"{WebChatEndpoint.MaxAllowedOrigins} websites; {supplied.Count} were supplied.")
            {
                Code = "website-chat.origin.too_many",
            };
        }

        List<WebChatOrigin> origins = new(supplied.Count);

        foreach (string candidate in supplied)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!WebChatOrigin.TryCreate(candidate, out WebChatOrigin origin, out string problem))
            {
                throw new DomainValidationException($"'{candidate.Trim()}': {problem}")
                {
                    Code = "website-chat.origin.invalid",
                };
            }

            origins.Add(origin);
        }

        return origins;
    }

    /// <summary>
    /// Reads a closed choice by name, refusing anything outside it.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because a client sending "left" meant Left, and
    /// <c>ignoreCase</c> on a parse that also checks the result is defined cannot
    /// let an ordinal through disguised as a name.
    /// </remarks>
    private static TValue Read<TValue>(string? value, string what, string code)
        where TValue : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out TValue parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new DomainValidationException(
                $"'{value.Trim()}' is not a recognised {what}.")
            {
                Code = code,
            };
        }

        return parsed;
    }
}
