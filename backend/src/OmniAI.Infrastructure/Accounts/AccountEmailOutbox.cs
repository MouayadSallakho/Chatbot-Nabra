using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Accounts;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Accounts;

/// <summary>One claimed account email.</summary>
/// <param name="Attempts">Including the one about to be made.</param>
internal sealed record ClaimedAccountEmail(
    AccountEmailId Id,
    AccountEmailKind Kind,
    UserId? UserId,
    TenantInvitationId? InvitationId,
    TenantId? TenantId,
    DateTimeOffset RequestedAt,
    Guid ClaimToken,
    int Attempts);

/// <summary>
/// Queued account emails, in PostgreSQL (Phase 37.3, M2 and M2a).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The claim rules of the operational outbox, unchanged.</strong> Claimed by
/// ownership (<c>FOR UPDATE SKIP LOCKED</c> and a claim token), dispatched across a
/// boundary that counts the attempt, settled only by the owner, and Unknown, never
/// Pending, after a Sending row outlives its lease. See
/// <see cref="NotificationEmailOutbox"/>.
/// </para>
/// <para>
/// <strong>Nothing secret is written here.</strong> No address, token, link,
/// subject or body. The one secret-shaped value it writes is the SHA-256 of an
/// invitation email's token, on the invitation, which is what acceptance compares.
/// </para>
/// </remarks>
internal sealed class AccountEmailOutbox(OmniAiDbContext context) : IAccountEmailOutbox
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task RequestPasswordResetAsync(
        string normalizedEmail,
        string subjectKey,
        ClientNetworkKey? client,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        ArgumentNullException.ThrowIfNull(subjectKey);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // 1. Holds the account's row, when there is one, so two requests for the same
        //    address cannot both find no pending email and both queue one. For an
        //    unknown address it locks nothing and costs the same index probe.
        await using (NpgsqlCommand hold = await Command(
                """
                SELECT id FROM users WHERE normalized_email = @normalized FOR UPDATE
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            hold.Parameters.AddWithValue("normalized", normalizedEmail);
            await hold.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        // 2. Queues the email if the account exists and has none pending, and writes
        //    the audit row either way, naming the account only when there is one.
        //    One statement, the same one whatever the address.
        await using (NpgsqlCommand request = await Command(
                """
                WITH target AS (
                    SELECT id FROM users WHERE normalized_email = @normalized
                ),
                queued AS (
                    INSERT INTO account_emails (id, user_id, kind, requested_at, status, available_at, attempts)
                    SELECT @email_id, target.id, 'PasswordReset', @now, 'Pending', @now, 0
                      FROM target
                     WHERE NOT EXISTS (
                           SELECT 1
                             FROM account_emails pending
                            WHERE pending.user_id = target.id
                              AND pending.kind = 'PasswordReset'
                              AND pending.status = 'Pending')
                    RETURNING id
                )
                INSERT INTO security_events (id, occurred_at, kind, user_id, subject_key, client_network_key)
                SELECT @event_id, @now, @kind, (SELECT id FROM target), @subject_key, @network_key
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            request.Parameters.AddWithValue("normalized", normalizedEmail);
            request.Parameters.AddWithValue("email_id", AccountEmailId.New().Value);
            request.Parameters.AddWithValue("now", now.ToUniversalTime());
            request.Parameters.AddWithValue("event_id", SecurityEventId.New().Value);
            request.Parameters.AddWithValue("kind", SecurityEventKinds.PasswordResetRequested);
            request.Parameters.AddWithValue("subject_key", subjectKey);
            request.Parameters.AddWithValue("network_key", (object?)client?.Value ?? DBNull.Value);

            await request.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task QueueAsync(AccountEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        await using NpgsqlCommand command = await Command(
                """
                INSERT INTO account_emails (id, user_id, invitation_id, tenant_id, kind, requested_at, status, available_at, attempts)
                VALUES (@id, @user_id, @invitation_id, @tenant_id, @kind, @requested_at, 'Pending', @requested_at, 0)
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", email.Id.Value);
        command.Parameters.AddWithValue("user_id", email.UserId is UserId user ? user.Value : DBNull.Value);
        command.Parameters.AddWithValue("invitation_id", email.InvitationId is { } invitation ? invitation.Value : DBNull.Value);
        command.Parameters.AddWithValue("tenant_id", email.TenantId is { } tenant ? tenant.Value : DBNull.Value);
        command.Parameters.AddWithValue("kind", email.Kind.ToString());
        command.Parameters.AddWithValue("requested_at", email.RequestedAt.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClaimedAccountEmail>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        DateTimeOffset reclaimClaimedBefore,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        await using NpgsqlCommand command = await Command(
                """
                WITH candidate AS MATERIALIZED (
                    SELECT id
                      FROM account_emails
                     WHERE status = 'Pending'
                       AND available_at <= @now
                       AND attempts < @max_attempts
                       AND (claim_token IS NULL OR claimed_at < @reclaim_before)
                     ORDER BY available_at, id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                )
                UPDATE account_emails e
                   SET claim_token = gen_random_uuid(),
                       claimed_at = @now
                  FROM candidate
                 WHERE e.id = candidate.id
                   AND e.status = 'Pending'
                RETURNING e.id, e.kind, e.user_id, e.invitation_id, e.tenant_id, e.requested_at, e.claim_token, e.attempts + 1
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("reclaim_before", reclaimClaimedBefore.ToUniversalTime());

        List<ClaimedAccountEmail> claimed = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedAccountEmail(
                AccountEmailId.From(reader.GetGuid(0)),
                Enum.Parse<AccountEmailKind>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : UserId.From(reader.GetGuid(2)),
                reader.IsDBNull(3) ? null : TenantInvitationId.From(reader.GetGuid(3)),
                reader.IsDBNull(4) ? null : TenantId.From(reader.GetGuid(4)),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6),
                reader.GetInt32(7)));
        }

        return claimed;
    }

    /// <summary>The dispatch boundary, as in the operational outbox.</summary>
    public async Task<bool> BeginDispatchAsync(
        AccountEmailId id,
        Guid claimToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE account_emails
                   SET status = 'Sending',
                       attempts = attempts + 1,
                       claimed_at = @now
                 WHERE id = @id
                   AND status = 'Pending'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Stores the hash of the token an invitation email will carry, and crosses the
    /// dispatch boundary, in one transaction.
    /// </summary>
    /// <remarks>
    /// Both or neither, and only while this worker still owns the email and the
    /// invitation is still open: a worker that lost its claim can never overwrite
    /// the hash of a message another worker has already sent.
    /// </remarks>
    /// <returns>False when the claim was lost or the invitation closed meanwhile.</returns>
    public async Task<bool> BeginInvitationDispatchAsync(
        ClaimedAccountEmail claimed,
        InvitationTokenHash emailTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(emailTokenHash);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (NpgsqlCommand record = await Command(
                """
                UPDATE tenant_invitations invitation
                   SET email_token_hash = @hash
                  FROM account_emails email
                 WHERE email.id = @id
                   AND email.status = 'Pending'
                   AND email.claim_token = @token
                   AND invitation.id = email.invitation_id
                   AND invitation.tenant_id = email.tenant_id
                   AND invitation.status = 'Pending'
                   AND invitation.expires_at > @now
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            record.Parameters.AddWithValue("hash", emailTokenHash.ToArray());
            record.Parameters.AddWithValue("id", claimed.Id.Value);
            record.Parameters.AddWithValue("token", claimed.ClaimToken);
            record.Parameters.AddWithValue("now", now.ToUniversalTime());

            if (await record.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                return false;
            }
        }

        if (!await BeginDispatchAsync(claimed.Id, claimed.ClaimToken, now, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> SettleAsync(
        AccountEmailId id,
        Guid claimToken,
        AccountEmailStatus status,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (status is AccountEmailStatus.Pending or AccountEmailStatus.Sending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only a final outcome settles an email.");
        }

        await using NpgsqlCommand command = await Command(
                """
                UPDATE account_emails
                   SET status = @status,
                       failure_code = @code,
                       sent_at = CASE WHEN @status = 'Sent' THEN @now ELSE NULL END,
                       settled_at = @now,
                       claim_token = NULL
                 WHERE id = @id
                   AND status = @from
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("from", status == AccountEmailStatus.Skipped ? "Pending" : "Sending");
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("code", Bounded(failureCode) is string code ? code : DBNull.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RetryLaterAsync(
        AccountEmailId id,
        Guid claimToken,
        string failureCode,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE account_emails
                   SET status = 'Pending',
                       failure_code = @code,
                       available_at = @available_at,
                       claim_token = NULL,
                       claimed_at = NULL
                 WHERE id = @id
                   AND status = 'Sending'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("code", Bounded(failureCode) ?? AccountEmailFailures.ConnectFailed);
        command.Parameters.AddWithValue("available_at", availableAt.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> AbandonStaleAsync(
        DateTimeOffset claimedBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE account_emails
                   SET status = 'Unknown',
                       failure_code = @code,
                       settled_at = @now,
                       claim_token = NULL
                 WHERE status = 'Sending'
                   AND claimed_at < @before
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("code", AccountEmailFailures.Abandoned);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("before", claimedBefore.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Bounded(string? code) =>
        code is null || code.Length <= AccountEmail.MaxFailureCodeLength
            ? code
            : code[..AccountEmail.MaxFailureCodeLength];

    private Task<NpgsqlCommand> Command(string sql, CancellationToken cancellationToken) =>
        NotificationOccurrenceSql.CommandAsync(_context, sql, cancellationToken);
}

/// <summary>Stable codes an account email can end with. Never transport text.</summary>
internal static class AccountEmailFailures
{
    public const string EmailNotConfigured = "email_not_configured";
    public const string AccountUnavailable = "account_unavailable";
    public const string RequestExpired = "request_expired";
    public const string InvitationClosed = "invitation_closed";
    public const string ConnectFailed = "connect_failed";
    public const string Rejected = "rejected";
    public const string Ambiguous = "outcome_unknown";
    public const string Abandoned = "abandoned_while_sending";
    public const string AttemptsExhausted = "attempts_exhausted";
}
