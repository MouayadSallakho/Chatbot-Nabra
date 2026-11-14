using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.Accounts;
using OmniAI.Application.Invitations;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Security;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reliability;
using OmniAI.Infrastructure.Reports;

namespace OmniAI.Infrastructure.Accounts;

/// <summary>
/// Sends one queued account email, deciding again whether it still should.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The secret is made here, at the last moment.</strong> A reset token is
/// minted from the account as it stands now, and an invitation token is minted and
/// its hash stored, only once the email is about to be handed to the server. The
/// token, the link and the message exist in this method's memory and nowhere else:
/// nothing is logged, and the queue row never held them.
/// </para>
/// <para>
/// <strong>Skipped rather than sent late or wrongly.</strong> An account that is
/// gone, a reset request older than its window, or an invitation that was accepted,
/// revoked or expired meanwhile each skip with a stable code.
/// </para>
/// <para>
/// <strong>The transport rules of the operational emails.</strong> A failure that
/// certainly sent nothing is retried a bounded number of times; one after the
/// message may have been handed over is Unknown and never retried. A reset that
/// ended Unknown is not resent either: the person can ask again.
/// </para>
/// </remarks>
internal sealed class AccountEmailDispatcher(
    AccountEmailOutbox queue,
    UserManager<OmniAiUser> users,
    OmniAiDbContext context,
    IInvitationTokens invitationTokens,
    IOperationalEmailSender sender,
    OperationalEmailLinks links,
    PasswordResetOptions resets,
    ISecurityEventLog audit,
    TimeProvider clock)
{
    /// <summary>The application routes the links open. They must match the client's.</summary>
    public const string ResetPasswordPath = "/reset-password";

    public const string AcceptInvitationPath = "/accept-invitation";

    public const string SignInPath = "/sign-in";

    private readonly AccountEmailOutbox _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly UserManager<OmniAiUser> _users = users ?? throw new ArgumentNullException(nameof(users));
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IInvitationTokens _invitationTokens = invitationTokens ?? throw new ArgumentNullException(nameof(invitationTokens));
    private readonly IOperationalEmailSender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    private readonly OperationalEmailLinks _links = links ?? throw new ArgumentNullException(nameof(links));
    private readonly PasswordResetOptions _resets = resets ?? throw new ArgumentNullException(nameof(resets));
    private readonly ISecurityEventLog _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>The status recorded, or null when the claim was no longer this worker's.</returns>
    public async Task<AccountEmailStatus?> SendAsync(
        ClaimedAccountEmail claimed,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        if (!_sender.IsConfigured)
        {
            return await SkipAsync(claimed, AccountEmailFailures.EmailNotConfigured, cancellationToken).ConfigureAwait(false);
        }

        return claimed.Kind == AccountEmailKind.Invitation
            ? await SendInvitationAsync(claimed, maxAttempts, cancellationToken).ConfigureAwait(false)
            : await SendToAccountAsync(claimed, maxAttempts, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountEmailStatus?> SendToAccountAsync(
        ClaimedAccountEmail claimed,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        OmniAiUser? user = claimed.UserId is UserId id
            ? await _users.FindByIdAsync(id.Value.ToString("D")).ConfigureAwait(false)
            : null;

        if (user?.Email is not string email || string.IsNullOrEmpty(user.NormalizedEmail))
        {
            return await SkipAsync(claimed, AccountEmailFailures.AccountUnavailable, cancellationToken).ConfigureAwait(false);
        }

        EmailAddress to = EmailAddress.FromStored(email, user.NormalizedEmail);
        UserLanguage language = UserLanguages.OrDefault(UserLanguages.FromStored(user.Language));
        OperationalEmailMessage message;

        if (claimed.Kind == AccountEmailKind.PasswordReset)
        {
            if (_clock.GetUtcNow() - claimed.RequestedAt > _resets.RequestLifetime)
            {
                return await SkipAsync(claimed, AccountEmailFailures.RequestExpired, cancellationToken).ConfigureAwait(false);
            }

            string token = await _users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

            Uri link = _links.WithFragment(
                ResetPasswordPath,
                string.Concat("uid=", user.Id.ToString("D"), "&token=", token));

            message = AccountEmailRenderer.PasswordReset(to, language, link, _resets.TokenLifetime);
        }
        else
        {
            message = AccountEmailRenderer.PasswordChanged(to, language, _links.For(SignInPath));
        }

        if (!await _queue.BeginDispatchAsync(claimed.Id, claimed.ClaimToken, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false))
        {
            return null;
        }

        return await DeliverAsync(claimed, message, maxAttempts, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountEmailStatus?> SendInvitationAsync(
        ClaimedAccountEmail claimed,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        TenantInvitation? invitation = claimed.InvitationId is TenantInvitationId invitationId && claimed.TenantId is { } tenantId
            ? await _context.TenantInvitations
                .AsNoTracking()
                .FirstOrDefaultAsync(row => row.Id == invitationId && row.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (invitation is null || !invitation.IsUsableAt(now))
        {
            return await SkipAsync(claimed, AccountEmailFailures.InvitationClosed, cancellationToken).ConfigureAwait(false);
        }

        string workspace = (await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(tenant => tenant.Id == invitation.TenantId, cancellationToken)
            .ConfigureAwait(false))?.Name.Value ?? string.Empty;

        NewInvitationToken token = _invitationTokens.Create();

        OperationalEmailMessage message = AccountEmailRenderer.Invitation(
            invitation.Email,
            UserLanguages.OrDefault(invitation.Language),
            workspace,
            invitation.ExpiresAt,
            _links.WithFragment(AcceptInvitationPath, "token=" + token.Token));

        // The hash is stored and the boundary crossed together, only while this
        // worker owns the email and the invitation is still open.
        if (!await _queue.BeginInvitationDispatchAsync(claimed, token.Hash, now, CancellationToken.None).ConfigureAwait(false))
        {
            return null;
        }

        return await DeliverAsync(claimed, message, maxAttempts, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountEmailStatus?> DeliverAsync(
        ClaimedAccountEmail claimed,
        OperationalEmailMessage message,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        OperationalEmailResult result = await _sender.SendAsync(message, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();

        AccountEmailStatus? status;

        switch (result.Outcome)
        {
            case OperationalEmailOutcome.Sent:
                status = await SettleAsync(claimed, AccountEmailStatus.Sent, null, now).ConfigureAwait(false);
                break;

            case OperationalEmailOutcome.NotSent when claimed.Attempts < maxAttempts:
                bool released = await _queue
                    .RetryLaterAsync(
                        claimed.Id,
                        claimed.ClaimToken,
                        result.FailureCode ?? AccountEmailFailures.ConnectFailed,
                        now + (OperationalEmailDispatcher.RetryBackoff * claimed.Attempts),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                return released ? AccountEmailStatus.Pending : null;

            case OperationalEmailOutcome.NotSent:
                status = await SettleAsync(claimed, AccountEmailStatus.Failed, AccountEmailFailures.AttemptsExhausted, now).ConfigureAwait(false);
                break;

            case OperationalEmailOutcome.Rejected:
                status = await SettleAsync(claimed, AccountEmailStatus.Failed, result.FailureCode ?? AccountEmailFailures.Rejected, now).ConfigureAwait(false);
                break;

            default:
                status = await SettleAsync(claimed, AccountEmailStatus.Unknown, AccountEmailFailures.Ambiguous, now).ConfigureAwait(false);
                break;
        }

        if (status is AccountEmailStatus settled)
        {
            await RecordAsync(claimed, settled, now, cancellationToken).ConfigureAwait(false);
        }

        return status;
    }

    /// <summary>The audit row for a sent or failed account email. None for a retry or a skip.</summary>
    private Task RecordAsync(ClaimedAccountEmail claimed, AccountEmailStatus status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool sent = status == AccountEmailStatus.Sent;

        string kind = claimed.Kind switch
        {
            AccountEmailKind.PasswordReset => sent ? SecurityEventKinds.PasswordResetEmailSent : SecurityEventKinds.PasswordResetEmailFailed,
            AccountEmailKind.PasswordChanged => sent ? SecurityEventKinds.PasswordChangedEmailSent : SecurityEventKinds.PasswordChangedEmailFailed,
            _ => sent ? SecurityEventKinds.InvitationEmailSent : SecurityEventKinds.InvitationEmailFailed,
        };

        return _audit.RecordAsync(
            SecurityEvent.Record(kind, now, userId: claimed.UserId, tenantId: claimed.TenantId),
            CancellationToken.None);
    }

    private Task<AccountEmailStatus?> SkipAsync(ClaimedAccountEmail claimed, string code, CancellationToken cancellationToken) =>
        SettleAsync(claimed, AccountEmailStatus.Skipped, code, _clock.GetUtcNow());

    /// <summary>Recorded even while the host is stopping: an outcome must not be lost.</summary>
    private async Task<AccountEmailStatus?> SettleAsync(
        ClaimedAccountEmail claimed,
        AccountEmailStatus status,
        string? code,
        DateTimeOffset now) =>
        await _queue.SettleAsync(claimed.Id, claimed.ClaimToken, status, code, now, CancellationToken.None).ConfigureAwait(false)
            ? status
            : null;
}

/// <summary>Sends queued account emails, on every instance at once.</summary>
/// <remarks>
/// Paced by the operational email settings (<c>Notifications:Workers</c>): the same
/// interval, batch, attempts and sending lease. Safe on several instances: claims
/// are exclusive, and a lapsed Sending row becomes Unknown, never Pending.
/// </remarks>
internal sealed partial class AccountEmailWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<AccountEmailWorker> logger)
    : BackgroundService
{
    private readonly NotificationWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                handled = await RunBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A worker that can die is account email that silently stops.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            if (handled == 0
                && !await ReportWorkerDelay.WaitAsync(_options.EmailInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedAccountEmail> claimed;
        DateTimeOffset now = clock.GetUtcNow();

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            AccountEmailOutbox queue = scope.ServiceProvider.GetRequiredService<AccountEmailOutbox>();

            int abandoned = await queue
                .AbandonStaleAsync(now - _options.EmailSendingLease, now, cancellationToken)
                .ConfigureAwait(false);

            if (abandoned > 0)
            {
                LogAbandoned(logger, abandoned);
            }

            claimed = await queue
                .ClaimAsync(_options.EmailBatchSize, _options.EmailMaxAttempts, now, now - _options.EmailSendingLease, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (ClaimedAccountEmail email in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("account-email");

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            AccountEmailStatus? status = await scope.ServiceProvider
                .GetRequiredService<AccountEmailDispatcher>()
                .SendAsync(email, _options.EmailMaxAttempts, cancellationToken)
                .ConfigureAwait(false);

            if (status is AccountEmailStatus.Failed or AccountEmailStatus.Unknown)
            {
                LogNotDelivered(logger, email.Id.Value, email.Kind, status.Value);
            }
        }

        return claimed.Count;
    }

    [LoggerMessage(EventId = 4310, Level = LogLevel.Error, Message = "An account email pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 4311, Level = LogLevel.Warning, Message = "{Count} account emails were left sending by a stopped worker and are now Unknown.")]
    private static partial void LogAbandoned(ILogger logger, int count);

    [LoggerMessage(EventId = 4312, Level = LogLevel.Warning, Message = "Account email {EmailId} ({Kind}) ended {Status}.")]
    private static partial void LogNotDelivered(ILogger logger, Guid emailId, AccountEmailKind kind, AccountEmailStatus status);
}

/// <summary>Registers the account services, the outbox and its worker (Phase 37.3).</summary>
public static class AccountsServiceCollectionExtensions
{
    /// <summary>After notifications (whose sender, links and worker settings it reuses).</summary>
    public static IServiceCollection AddOmniAiAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAccountLanguages, AccountLanguages>();
        services.AddScoped<IAccountDisplayNames, AccountDisplayNames>();
        services.AddScoped<ISecurityEventLog, SecurityEventLog>();

        services.AddScoped<AccountEmailOutbox>();
        services.AddScoped<IAccountEmailOutbox>(provider => provider.GetRequiredService<AccountEmailOutbox>());
        services.AddScoped<AccountEmailDispatcher>();
        services.AddHostedService<AccountEmailWorker>();

        return services;
    }
}
