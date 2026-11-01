using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using OmniAI.Api.Security;
using OmniAI.Application.Accounts;
using OmniAI.Application.Notifications;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Security;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Accounts;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Api.Auth;

/// <summary>
/// Asking for a password reset, and using the link (Phase 37.3).
/// </summary>
/// <remarks>
/// <para>
/// <strong>No user enumeration.</strong> <c>forgot</c> answers 202 with no body for
/// every address. The limiter counts unknown addresses like known ones; the
/// database does the same two statements either way (<see cref="IAccountEmailOutbox"/>);
/// and every answer is padded to <see cref="PasswordResetOptions.MinimumResponseTime"/>,
/// so the little work that differs cannot be timed.
/// </para>
/// <para>
/// <strong>The link never touches a server log.</strong> It is built only when the
/// email is sent, and carries the account and the token in its fragment, which a
/// browser never sends anywhere. <c>reset</c> receives them in a JSON body. Neither
/// endpoint logs a body, and no audit row holds an address, a token or a password.
/// </para>
/// <para>
/// <strong>One refusal for every bad link.</strong> A token that is malformed,
/// altered, expired or already used, and an account that does not exist, are all
/// <c>400 auth.reset.invalid</c>. Password rules are reported (<c>auth.password.*</c>)
/// only once the token has been accepted.
/// </para>
/// </remarks>
internal static class PasswordResetEndpoints
{
    /// <summary><c>POST /api/auth/password/forgot</c>.</summary>
    public static async Task<IResult> ForgotAsync(
        HttpContext http,
        ForgotPasswordRequest request,
        UserManager<OmniAiUser> users,
        IAccountProtection protection,
        IAccountSubjectKeys subjects,
        IClientNetworkKeys networks,
        IAccountEmailOutbox outbox,
        ISecurityEventLog audit,
        PasswordResetOptions options,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);

        long started = Stopwatch.GetTimestamp();
        IResult answer;

        try
        {
            // Identity's own normaliser, so the address is matched exactly as a
            // sign-in would match it. A blank or malformed one matches no account
            // and is otherwise treated like any other.
            string normalized = users.NormalizeEmail((request.Email ?? string.Empty).Trim()) ?? string.Empty;
            string subject = subjects.Derive(normalized);
            ClientNetworkKey? client = ClientNetworkIdentity.Resolve(http, networks);
            DateTimeOffset now = clock.GetUtcNow();

            AccountProtectionDecision decision = await protection
                .AuthorisePasswordResetRequestAsync(subject, client, http.RequestAborted)
                .ConfigureAwait(false);

            if (decision.Allowed)
            {
                await outbox
                    .RequestPasswordResetAsync(normalized, subject, client, now, http.RequestAborted)
                    .ConfigureAwait(false);

                answer = Results.Accepted();
            }
            else
            {
                await audit
                    .RecordAsync(
                        SecurityEvent.Record(
                            SecurityEventKinds.PasswordResetThrottled,
                            now,
                            subjectKey: subject,
                            clientNetworkKey: client?.Value),
                        http.RequestAborted)
                    .ConfigureAwait(false);

                answer = AccountProblems.TooMany(http, decision);
            }
        }
        catch (Exception unprotected) when (unprotected is AccountProtectionUnavailableException or PublicChatProtectionUnavailableException)
        {
            answer = AccountProblems.Unavailable(loggers.CreateLogger(typeof(PasswordResetEndpoints)), unprotected);
        }

        await PadAsync(started, options.MinimumResponseTime, clock, http.RequestAborted).ConfigureAwait(false);

        return answer;
    }

    /// <summary><c>POST /api/auth/password/reset</c>.</summary>
    /// <remarks>
    /// On success: the stamp rotates (Identity), the new stamp is cached before the
    /// answer so the next request on any instance is refused for an old cookie, the
    /// account's realtime connections close on every instance, a lockout is lifted,
    /// a "your password was changed" email is queued, and two audit rows are
    /// written. The person then signs in with the new password.
    /// </remarks>
    public static async Task<IResult> ResetAsync(
        HttpContext http,
        ResetPasswordRequest request,
        UserManager<OmniAiUser> users,
        IAccountProtection protection,
        IClientNetworkKeys networks,
        SessionStamps stamps,
        ISessionRevocation revocation,
        IAccountEmailOutbox outbox,
        IOperationalEmailSender email,
        ISecurityEventLog audit,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(stamps);
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);

        ClientNetworkKey? client;

        try
        {
            client = ClientNetworkIdentity.Resolve(http, networks);

            AccountProtectionDecision decision = await protection
                .AuthorisePasswordResetAsync(client, http.RequestAborted)
                .ConfigureAwait(false);

            if (!decision.Allowed)
            {
                return AccountProblems.TooMany(http, decision);
            }
        }
        catch (Exception unprotected) when (unprotected is AccountProtectionUnavailableException or PublicChatProtectionUnavailableException)
        {
            return AccountProblems.Unavailable(loggers.CreateLogger(typeof(PasswordResetEndpoints)), unprotected);
        }

        OmniAiUser? user = Guid.TryParse(request.UserId, out Guid id) && !string.IsNullOrEmpty(request.Token)
            ? await users.FindByIdAsync(id.ToString("D")).ConfigureAwait(false)
            : null;

        if (user is null)
        {
            return await RejectedAsync(audit, null, client, clock, http.RequestAborted).ConfigureAwait(false);
        }

        UserId userId = UserId.From(user.Id);

        IdentityResult result = await users
            .ResetPasswordAsync(user, request.Token!, request.NewPassword ?? string.Empty)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Identity checks the token before the password, so a password rule
            // can only be reported once the link has been accepted.
            return result.Errors.Any(error => AccountProblems.CodeFor(error) == AccountProblems.ResetInvalid)
                ? await RejectedAsync(audit, userId, client, clock, http.RequestAborted).ConfigureAwait(false)
                : AccountProblems.FromIdentity("Password rejected", result.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();

        // Somebody who just proved they hold the inbox is not left locked out by
        // failed sign-ins that may have been somebody else's.
        await users.SetLockoutEndDateAsync(user, null).ConfigureAwait(false);
        await users.ResetAccessFailedCountAsync(user).ConfigureAwait(false);

        await stamps.RecordAsync(user.Id, await users.GetSecurityStampAsync(user).ConfigureAwait(false)).ConfigureAwait(false);
        await revocation.RevokeAsync(userId, CancellationToken.None).ConfigureAwait(false);

        if (email.IsConfigured)
        {
            await outbox
                .QueueAsync(AccountEmail.ForUser(userId, AccountEmailKind.PasswordChanged, now), CancellationToken.None)
                .ConfigureAwait(false);
        }

        await audit
            .RecordAsync(SecurityEvent.Record(SecurityEventKinds.PasswordResetCompleted, now, userId: userId, clientNetworkKey: client?.Value), CancellationToken.None)
            .ConfigureAwait(false);
        await audit
            .RecordAsync(SecurityEvent.Record(SecurityEventKinds.SessionsRevoked, now, userId: userId, clientNetworkKey: client?.Value), CancellationToken.None)
            .ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> RejectedAsync(
        ISecurityEventLog audit,
        UserId? userId,
        ClientNetworkKey? client,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await audit
            .RecordAsync(
                SecurityEvent.Record(SecurityEventKinds.PasswordResetRejected, clock.GetUtcNow(), userId: userId, clientNetworkKey: client?.Value),
                cancellationToken)
            .ConfigureAwait(false);

        return AccountProblems.Problem(
            StatusCodes.Status400BadRequest,
            "Reset link not valid",
            "This password reset link is not valid. It may have expired or been used already.",
            AccountProblems.ResetInvalid);
    }

    /// <summary>Waits until at least <paramref name="minimum"/> has passed since <paramref name="started"/>.</summary>
    /// <remarks>
    /// A loop, because a timer may fire a few milliseconds early: the floor is
    /// measured by the stopwatch, not by the timer, so no answer leaves before it.
    /// </remarks>
    private static async Task PadAsync(long started, TimeSpan minimum, TimeProvider clock, CancellationToken cancellationToken)
    {
        try
        {
            for (TimeSpan remaining = minimum - Stopwatch.GetElapsedTime(started);
                remaining > TimeSpan.Zero;
                remaining = minimum - Stopwatch.GetElapsedTime(started))
            {
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining, clock, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller left; nobody is waiting for the answer.
        }
    }
}
