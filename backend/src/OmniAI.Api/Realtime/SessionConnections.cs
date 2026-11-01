using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using OmniAI.Api.Security;
using OmniAI.Application.Accounts;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Realtime;

/// <summary>
/// This instance's open admin hub connections, by account (Phase 37.3).
/// </summary>
/// <remarks>
/// A SignalR connection is authenticated once, when it opens. To end it when its
/// account's sessions end, each instance must be able to find it, and only the
/// instance holding it can: this registry is that instance's list.
/// </remarks>
internal sealed class SessionConnections
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, HubCallerContext>> _byUser = new();

    public void Add(Guid userId, HubCallerContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, HubCallerContext>(StringComparer.Ordinal))
            [connection.ConnectionId] = connection;
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (_byUser.TryGetValue(userId, out ConcurrentDictionary<string, HubCallerContext>? connections))
        {
            connections.TryRemove(connectionId, out _);
        }
    }

    /// <returns>How many connections were closed.</returns>
    public int AbortAll(Guid userId)
    {
        if (!_byUser.TryRemove(userId, out ConcurrentDictionary<string, HubCallerContext>? connections))
        {
            return 0;
        }

        foreach (HubCallerContext connection in connections.Values)
        {
            connection.Abort();
        }

        return connections.Count;
    }

    /// <summary>How many connections an account has open here. For tests and diagnostics.</summary>
    public int CountFor(Guid userId) =>
        _byUser.TryGetValue(userId, out ConcurrentDictionary<string, HubCallerContext>? connections) ? connections.Count : 0;
}

/// <summary>
/// Keeps <see cref="SessionConnections"/> current, and re-checks the session stamp
/// on every hub invocation.
/// </summary>
/// <remarks>
/// <strong>Two defences, because either alone has a gap.</strong> The revocation
/// message closes connections promptly, but a message can be missed during a Redis
/// blip; the per-invocation check refuses anything a stale connection tries, even
/// then. Applies to the admin hub only: the public chat hub has no account.
/// </remarks>
internal sealed class SessionStampHubFilter(SessionConnections connections) : IHubFilter
{
    private readonly SessionConnections _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        if (invocationContext.Hub is WorkspaceInboxHub
            && !await IsCurrentAsync(invocationContext.Context, invocationContext.ServiceProvider).ConfigureAwait(false))
        {
            invocationContext.Context.Abort();

            throw new HubException("This session has ended.");
        }

        return await next(invocationContext).ConfigureAwait(false);
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Hub is WorkspaceInboxHub && UserIdOf(context.Context, context.ServiceProvider) is Guid userId)
        {
            _connections.Add(userId, context.Context);
        }

        return next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Hub is WorkspaceInboxHub && UserIdOf(context.Context, context.ServiceProvider) is Guid userId)
        {
            _connections.Remove(userId, context.Context.ConnectionId);
        }

        return next(context, exception);
    }

    private static async Task<bool> IsCurrentAsync(HubCallerContext connection, IServiceProvider services)
    {
        IdentityOptions identity = services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        string? presented = connection.User?.FindFirstValue(identity.ClaimsIdentity.SecurityStampClaimType);

        if (UserIdOf(connection, services) is not Guid userId)
        {
            return false;
        }

        SessionStamps stamps = services.GetRequiredService<SessionStamps>();
        string? current = await stamps.CurrentAsync(userId, connection.ConnectionAborted).ConfigureAwait(false);

        if (SessionStamps.Matches(presented, current))
        {
            return true;
        }

        // As for HTTP: a cached stamp is confirmed against the database before a
        // connection is refused for it.
        return current is not null
            && SessionStamps.Matches(
                presented,
                await stamps.CurrentAsync(userId, connection.ConnectionAborted, fromDatabase: true).ConfigureAwait(false));
    }

    private static Guid? UserIdOf(HubCallerContext connection, IServiceProvider services)
    {
        IdentityOptions identity = services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        return Guid.TryParse(connection.User?.FindFirstValue(identity.ClaimsIdentity.UserIdClaimType), out Guid userId)
            ? userId
            : null;
    }
}

/// <summary>
/// Ends an account's realtime connections here at once, and on every other
/// instance through Redis.
/// </summary>
internal sealed partial class SessionRevocation(
    SessionConnections connections,
    IServiceProvider services,
    ILogger<SessionRevocation> logger) : ISessionRevocation
{
    /// <summary>The Redis channel every instance listens on.</summary>
    public static readonly RedisChannel Channel = RedisChannel.Literal("omniai:auth:revoked");

    private readonly SessionConnections _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    private readonly IConnectionMultiplexer? _redis = services.GetService<IConnectionMultiplexer>();

    public async Task RevokeAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        _connections.AbortAll(userId.Value);

        if (_redis is null)
        {
            return;
        }

        try
        {
            await _redis.GetSubscriber()
                .PublishAsync(Channel, userId.Value.ToString("N"))
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (SessionStamps.IsStoreFailure(failure, cancellationToken))
        {
            // The other instances still refuse every invocation of a stale
            // connection (SessionStampHubFilter); they only close it later.
            LogNotPublished(logger, failure);
        }
    }

    [LoggerMessage(EventId = 4330, Level = LogLevel.Warning, Message = "A session revocation could not be published to the other instances.")]
    private static partial void LogNotPublished(ILogger logger, Exception failure);
}

/// <summary>Listens for revocations from every instance, including this one.</summary>
internal sealed partial class SessionRevocationListener(
    SessionConnections connections,
    IServiceProvider services,
    ILogger<SessionRevocationListener> logger) : IHostedService
{
    private readonly IConnectionMultiplexer? _redis = services.GetService<IConnectionMultiplexer>();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_redis is null)
        {
            return;
        }

        try
        {
            await _redis.GetSubscriber()
                .SubscribeAsync(SessionRevocation.Channel, (_, message) =>
                {
                    if (Guid.TryParseExact(message.ToString(), "N", out Guid userId))
                    {
                        connections.AbortAll(userId);
                    }
                })
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (SessionStamps.IsStoreFailure(failure, cancellationToken))
        {
            // The per-invocation stamp check still refuses stale connections.
            LogNotSubscribed(logger, failure);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_redis is null)
        {
            return;
        }

        try
        {
            await _redis.GetSubscriber().UnsubscribeAsync(SessionRevocation.Channel).ConfigureAwait(false);
        }
        catch (Exception failure) when (SessionStamps.IsStoreFailure(failure, cancellationToken))
        {
            LogNotSubscribed(logger, failure);
        }
    }

    [LoggerMessage(EventId = 4331, Level = LogLevel.Warning, Message = "Session revocations from other instances could not be subscribed to.")]
    private static partial void LogNotSubscribed(ILogger logger, Exception failure);
}
