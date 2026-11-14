using System.Buffers.Text;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using OmniAI.Application.Accounts;
using OmniAI.Domain.Security;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.WebChat.Protection;

namespace OmniAI.Infrastructure.Accounts;

/// <summary>
/// Writes <c>users.language</c> (Phase 37.3, M1) with one conditional statement each.
/// </summary>
/// <remarks>
/// <strong>Not through <c>UserManager</c>.</strong> A language is not a credential:
/// it needs no validators, no concurrency stamp and no security stamp, and an
/// <c>UPDATE … WHERE language IS NULL</c> is the only way "fill it only while it is
/// empty" holds when two first sign-ins race.
/// </remarks>
internal sealed class AccountLanguages(OmniAiDbContext context) : IAccountLanguages
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> SetAsync(UserId userId, UserLanguage language, CancellationToken cancellationToken = default)
    {
        string code = UserLanguages.Code(language);

        int rows = await _context.Users
            .Where(user => user.Id == userId.Value)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.Language, code), cancellationToken)
            .ConfigureAwait(false);

        return rows == 1;
    }

    public async Task FillIfUnsetAsync(UserId userId, UserLanguage language, CancellationToken cancellationToken = default)
    {
        string code = UserLanguages.Code(language);

        await _context.Users
            .Where(user => user.Id == userId.Value && user.Language == null)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.Language, code), cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Writes <c>users.display_name</c> (Phase 39.1) with one statement.
/// </summary>
/// <remarks>
/// Not through <c>UserManager</c>, for the reason <see cref="AccountLanguages"/>
/// gives: a display name is not a credential, so it touches no stamp.
/// </remarks>
internal sealed class AccountDisplayNames(OmniAiDbContext context) : IAccountDisplayNames
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> SetAsync(UserId userId, MemberDisplayName? displayName, CancellationToken cancellationToken = default)
    {
        string? value = displayName?.Value;

        int rows = await _context.Users
            .Where(user => user.Id == userId.Value)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.DisplayName, value), cancellationToken)
            .ConfigureAwait(false);

        return rows == 1;
    }
}

/// <summary>
/// The audit table's writer. One <c>INSERT</c>; a failure is logged and swallowed.
/// </summary>
/// <remarks>
/// Written in SQL rather than through the context, so recording an event can never
/// save anything else the request happens to be tracking. When the request has a
/// transaction open, the record joins it and stands or falls with what it describes.
/// </remarks>
internal sealed partial class SecurityEventLog(OmniAiDbContext context, ILogger<SecurityEventLog> logger) : ISecurityEventLog
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        try
        {
            await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                    _context,
                    """
                    INSERT INTO security_events (id, occurred_at, kind, user_id, tenant_id, subject_key, client_network_key)
                    VALUES (@id, @occurred_at, @kind, @user_id, @tenant_id, @subject_key, @client_network_key)
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            command.Parameters.AddWithValue("id", securityEvent.Id.Value);
            command.Parameters.AddWithValue("occurred_at", securityEvent.OccurredAt);
            command.Parameters.AddWithValue("kind", securityEvent.Kind);
            command.Parameters.AddWithValue("user_id", securityEvent.UserId is UserId user ? user.Value : DBNull.Value);
            command.Parameters.AddWithValue("tenant_id", securityEvent.TenantId is { } tenant ? tenant.Value : DBNull.Value);
            command.Parameters.AddWithValue("subject_key", (object?)securityEvent.SubjectKey ?? DBNull.Value);
            command.Parameters.AddWithValue("client_network_key", (object?)securityEvent.ClientNetworkKey ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException failure)
        {
            LogNotRecorded(logger, securityEvent.Kind, failure);
        }
        catch (InvalidOperationException failure)
        {
            LogNotRecorded(logger, securityEvent.Kind, failure);
        }
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "A security event of kind {Kind} could not be recorded.")]
    private static partial void LogNotRecorded(ILogger logger, string kind, Exception failure);
}

/// <summary>
/// Keyed hashes of normalised addresses: HMAC-SHA256 under a key derived from the
/// network-key secret with its own label, truncated to 128 bits, base64url.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why keyed.</strong> A plain hash of an address is a lookup table away
/// from the address. Under a secret key, the counters and the audit rows can be
/// read without telling anybody which addresses were tried.
/// </para>
/// <para>
/// <strong>Why a derived key.</strong> The same secret keys the network hashes; a
/// distinct label keeps the two uses from ever producing comparable values, and
/// needs no new secret.
/// </para>
/// </remarks>
internal sealed class HmacAccountSubjectKeys : IAccountSubjectKeys
{
    private const string Label = "nabra:account-subject:v1";

    private const int KeyBytes = 16;

    private readonly byte[]? _key;
    private readonly string? _unavailable;

    public HmacAccountSubjectKeys(NetworkKeySecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.Value is string value)
        {
            _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(Label));
        }
        else
        {
            _unavailable = secret.Unavailable;
        }
    }

    public string Derive(string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);

        if (_key is null)
        {
            throw new AccountProtectionUnavailableException(
                _unavailable ?? "No rate-limit key secret is configured.");
        }

        Span<byte> digest = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(normalizedEmail), digest);

        return Base64Url.EncodeToString(digest[..KeyBytes]);
    }
}
