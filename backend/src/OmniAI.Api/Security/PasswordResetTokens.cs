using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OmniAI.Infrastructure.Accounts;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Api.Security;

/// <summary>The reset provider's own options type, so no other provider shares them.</summary>
internal sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
}

/// <summary>
/// ASP.NET Core Identity's password-reset token, from a provider of its own.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it is.</strong> A data-protected payload (creation time, user id,
/// purpose, security stamp) under the platform's key ring, which both production
/// APIs share, so a link minted by one validates on the other. Nothing about it is
/// stored: there is nothing at rest to hash.
/// </para>
/// <para>
/// <strong>Single use.</strong> It carries the security stamp, and a successful
/// reset rotates the stamp, so the token and every earlier one stop validating at
/// once. It also expires after <see cref="PasswordResetOptions.TokenLifetime"/>.
/// </para>
/// <para>
/// <strong>Why it writes its own payload.</strong> The base class reads the clock
/// itself and compares the id and the stamp with ordinary string equality. This
/// one takes the platform's <see cref="TimeProvider"/>, compares both in constant
/// time, and encodes the token as base64url so it travels in a URL fragment
/// unescaped. The format and the protector are otherwise the base class's.
/// </para>
/// </remarks>
internal sealed class PasswordResetTokenProvider : DataProtectorTokenProvider<OmniAiUser>
{
    /// <summary>The provider's name, and the data-protection purpose of its protector.</summary>
    public const string ProviderName = "NabraPasswordReset";

    private readonly TimeProvider _clock;

    public PasswordResetTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PasswordResetTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<OmniAiUser>> logger,
        TimeProvider clock)
        : base(dataProtectionProvider, options, logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public override async Task<string> GenerateAsync(string purpose, UserManager<OmniAiUser> manager, OmniAiUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        string userId = await manager.GetUserIdAsync(user).ConfigureAwait(false);
        string stamp = manager.SupportsUserSecurityStamp
            ? await manager.GetSecurityStampAsync(user).ConfigureAwait(false) ?? string.Empty
            : string.Empty;

        using MemoryStream stream = new();

        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(_clock.GetUtcNow().UtcTicks);
            writer.Write(userId);
            writer.Write(purpose ?? string.Empty);
            writer.Write(stamp);
        }

        return Base64Url.EncodeToString(Protector.Protect(stream.ToArray()));
    }

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<OmniAiUser> manager, OmniAiUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return false;
        }

        long created;
        string tokenUserId;
        string tokenPurpose;
        string tokenStamp;

        try
        {
            byte[] payload = Protector.Unprotect(Base64Url.DecodeFromChars(token));

            using MemoryStream stream = new(payload);
            using BinaryReader reader = new(stream, Encoding.UTF8);

            created = reader.ReadInt64();
            tokenUserId = reader.ReadString();
            tokenPurpose = reader.ReadString();
            tokenStamp = reader.ReadString();

            if (stream.Position != stream.Length)
            {
                return false;
            }
        }
        catch (Exception unreadable) when (unreadable is CryptographicException or FormatException or EndOfStreamException or IOException)
        {
            // A token from another key ring, an altered one, or plain noise: all
            // the same answer, and nothing about which.
            return false;
        }

        DateTimeOffset createdAt = new(created, TimeSpan.Zero);

        if (createdAt + Options.TokenLifespan < _clock.GetUtcNow())
        {
            return false;
        }

        string userId = await manager.GetUserIdAsync(user).ConfigureAwait(false);
        string stamp = manager.SupportsUserSecurityStamp
            ? await manager.GetSecurityStampAsync(user).ConfigureAwait(false) ?? string.Empty
            : string.Empty;

        // Every comparison runs, and the two secrets are compared in constant time.
        bool sameUser = FixedTimeEquals(tokenUserId, userId);
        bool samePurpose = string.Equals(tokenPurpose, purpose ?? string.Empty, StringComparison.Ordinal);
        bool sameStamp = FixedTimeEquals(tokenStamp, stamp);

        return sameUser & samePurpose & sameStamp;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

/// <summary>Registers password reset: the provider, its options and its lifetime.</summary>
internal static class PasswordResetSetup
{
    /// <summary>
    /// The dedicated reset provider, and only it. No other token provider exists in
    /// this platform, so no other token's lifetime changes.
    /// </summary>
    public static IdentityBuilder AddNabraPasswordReset(
        this IdentityBuilder builder,
        IConfiguration configuration,
        bool development)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        PasswordResetOptions options = new();
        configuration.GetSection(PasswordResetOptions.SectionName).Bind(options);
        options.Validate(development);

        builder.Services.AddSingleton(options);

        builder.Services.Configure<PasswordResetTokenProviderOptions>(provider =>
        {
            provider.Name = PasswordResetTokenProvider.ProviderName;
            provider.TokenLifespan = options.TokenLifetime;
        });

        builder.Services.Configure<IdentityOptions>(identity =>
            identity.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName);

        return builder.AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);
    }
}
