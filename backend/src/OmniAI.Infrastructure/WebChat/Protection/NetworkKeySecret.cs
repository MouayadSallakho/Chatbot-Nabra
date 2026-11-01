namespace OmniAI.Infrastructure.WebChat.Protection;

/// <summary>
/// The rate-limit key secret, read once per process: configured, ephemeral in
/// Development, or missing with the reason why.
/// </summary>
/// <remarks>
/// <strong>One secret behind two key sources (Phase 37.3).</strong> The public
/// chat's network keys use it directly; the account limiter's address keys derive
/// a key of their own from it under a distinct label. No new secret and no
/// <c>deploy/</c> change was needed for the second.
/// </remarks>
internal sealed class NetworkKeySecret
{
    private NetworkKeySecret(string? value, string? unavailable)
    {
        Value = value;
        Unavailable = unavailable;
    }

    /// <summary>The secret; null when none is configured.</summary>
    public string? Value { get; }

    /// <summary>Why there is none, for the refusal it causes.</summary>
    public string? Unavailable { get; }

    public static NetworkKeySecret Configured(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new NetworkKeySecret(value, null);
    }

    public static NetworkKeySecret Missing(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new NetworkKeySecret(null, reason);
    }
}
