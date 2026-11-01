using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.Security;

/// <summary>
/// The current security stamp of an account, from Redis or the database.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a stamp ends every session at once (Phase 37.3).</strong> Every
/// session cookie carries the stamp its account had when it was issued. A password
/// reset rotates the stamp, and every request compares the two, so each cookie
/// issued before the reset stops working on its next request, on every instance.
/// <c>users.security_stamp</c> already is the "valid after" marker; no column was
/// added.
/// </para>
/// <para>
/// <strong>Redis first, the database when it must.</strong> The stamp is read from
/// <c>omniai:auth:stamp:v1:{userId}</c>, and on a miss or a Redis failure from
/// <c>users</c>. A value read from the database is cached only if no newer one is
/// there (<c>SET NX</c>), and a reset writes its stamp unconditionally before it
/// answers, so a stale read can never overwrite a fresh stamp. A short expiry bounds
/// any copy that a failed write left behind.
/// </para>
/// </remarks>
internal sealed partial class SessionStamps(
    OmniAiDbContext context,
    IServiceProvider services,
    ILogger<SessionStamps> logger)
{
    /// <summary>How long a cached stamp lives without being written again.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    // Optional: a deployment without the protection Redis reads the database.
    private readonly IConnectionMultiplexer? _redis = services.GetService<IConnectionMultiplexer>();

    public static string Key(Guid userId) => $"omniai:auth:stamp:v1:{userId:N}";

    /// <param name="fromDatabase">True to skip the cache, after the cached value did not match.</param>
    /// <returns>The stamp, or null when the account does not exist.</returns>
    public async Task<string?> CurrentAsync(Guid userId, CancellationToken cancellationToken, bool fromDatabase = false)
    {
        if (_redis is not null && !fromDatabase)
        {
            try
            {
                RedisValue cached = await _redis.GetDatabase().StringGetAsync(Key(userId)).ConfigureAwait(false);

                if (cached.HasValue)
                {
                    return cached.ToString();
                }
            }
            catch (Exception failure) when (IsStoreFailure(failure, cancellationToken))
            {
                LogCacheUnavailable(logger, failure);
            }
        }

        string? stored = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null && _redis is not null)
        {
            try
            {
                await _redis.GetDatabase()
                    .StringSetAsync(Key(userId), stored, CacheLifetime, When.NotExists)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (IsStoreFailure(failure, cancellationToken))
            {
                LogCacheUnavailable(logger, failure);
            }
        }

        return stored;
    }

    /// <summary>Stores a new stamp, overwriting whatever was cached. Called by a reset before it answers.</summary>
    public async Task RecordAsync(Guid userId, string stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);

        if (_redis is null)
        {
            return;
        }

        try
        {
            await _redis.GetDatabase().StringSetAsync(Key(userId), stamp, CacheLifetime).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsStoreFailure(failure))
        {
            // The old stamp may survive in the cache until it expires; the reset
            // still succeeded, and the database holds the new one.
            LogStampNotCached(logger, failure);
        }
    }

    /// <summary>Whether two stamps are the same, compared in constant time.</summary>
    public static bool Matches(string? presented, string? current) =>
        presented is not null
        && current is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(current));

    /// <summary>
    /// The cookie's check on every request: the stamp it carries against the
    /// account's current one. A mismatch, a missing claim or a missing account
    /// signs the cookie out.
    /// </summary>
    /// <remarks>
    /// It replaces Identity's interval validator, which re-checked the stamp only
    /// every thirty minutes. That validator also re-issued the cookie each time it
    /// checked, which kept an active session's eight-hour window moving; the same
    /// cadence is kept here (<see cref="RenewIfDue"/>), so the cookie is re-issued
    /// at most once per validation interval and never on every request.
    /// </remarks>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ClaimsPrincipal? principal = context.Principal;
        IdentityOptions identity = context.HttpContext.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value;

        string? id = principal?.FindFirstValue(identity.ClaimsIdentity.UserIdClaimType);
        string? presented = principal?.FindFirstValue(identity.ClaimsIdentity.SecurityStampClaimType);

        string? current = Guid.TryParse(id, out Guid userId)
            ? await context.HttpContext.RequestServices
                .GetRequiredService<SessionStamps>()
                .CurrentAsync(userId, context.HttpContext.RequestAborted)
                .ConfigureAwait(false)
            : null;

        if (Matches(presented, current))
        {
            RenewIfDue(context);

            return;
        }

        // A mismatch is checked once more against the database before it signs
        // anybody out: a cached stamp left behind by a failed write must not lock a
        // freshly signed-in session out until it expires. When the database agrees
        // with the cookie, the cache is repaired.
        if (current is not null)
        {
            SessionStamps stamps = context.HttpContext.RequestServices.GetRequiredService<SessionStamps>();
            string? stored = await stamps.CurrentAsync(userId, context.HttpContext.RequestAborted, fromDatabase: true).ConfigureAwait(false);

            if (stored is not null && Matches(presented, stored))
            {
                await stamps.RecordAsync(userId, stored).ConfigureAwait(false);
                RenewIfDue(context);

                return;
            }
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-issues a valid cookie once its validation interval (Identity's
    /// <see cref="SecurityStampValidatorOptions.ValidationInterval"/>, thirty minutes)
    /// has passed since it was issued, as Identity's validator did before Phase 37.3.
    /// </summary>
    private static void RenewIfDue(CookieValidatePrincipalContext context)
    {
        TimeSpan interval = context.HttpContext.RequestServices
            .GetService<IOptions<SecurityStampValidatorOptions>>()?.Value.ValidationInterval
            ?? TimeSpan.FromMinutes(30);

        TimeProvider clock = context.Options.TimeProvider ?? TimeProvider.System;

        if (context.Properties.IssuedUtc is not DateTimeOffset issued || clock.GetUtcNow() - issued > interval)
        {
            context.ShouldRenew = true;
        }
    }

    /// <summary>A Redis outage, as the protection limiters classify one; never the caller's own cancellation.</summary>
    internal static bool IsStoreFailure(Exception failure, CancellationToken caller = default) =>
        failure is RedisException or ObjectDisposedException or TimeoutException
            or System.Net.Sockets.SocketException or IOException
        || (failure is OperationCanceledException && !caller.IsCancellationRequested);

    [LoggerMessage(EventId = 4320, Level = LogLevel.Warning, Message = "The session stamp cache could not be reached; the database was read instead.")]
    private static partial void LogCacheUnavailable(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 4321, Level = LogLevel.Warning, Message = "A new session stamp could not be cached; a session issued before it may be accepted until the cached copy expires.")]
    private static partial void LogStampNotCached(ILogger logger, Exception failure);
}
