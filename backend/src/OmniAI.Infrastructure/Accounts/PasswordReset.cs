using System.Globalization;

namespace OmniAI.Infrastructure.Accounts;

/// <summary>
/// Password reset settings. Section <c>Auth:PasswordReset</c>.
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "Auth:PasswordReset";

    /// <summary>How long a reset link works: 30 minutes, bounded to 10–60 (1–60 in Development).</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>A request older than this when its email is about to be sent is skipped, not sent late.</summary>
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Every answer to a reset request takes at least this long, so the work done
    /// for a known address cannot be told from the work done for an unknown one.
    /// </summary>
    public TimeSpan MinimumResponseTime { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <param name="development">
    /// True in Development only, where a lifetime down to one minute is allowed so
    /// an expired link can be checked by hand (owner decision, 2026-10-21).
    /// </param>
    public void Validate(bool development)
    {
        TimeSpan shortest = development ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(10);

        Require(
            TokenLifetime >= shortest && TokenLifetime <= TimeSpan.FromMinutes(60),
            string.Create(CultureInfo.InvariantCulture, $"TokenLifetime must be between {shortest.TotalMinutes} and 60 minutes."));
        Require(
            RequestLifetime >= TimeSpan.FromMinutes(1) && RequestLifetime <= TokenLifetime + TimeSpan.FromMinutes(30),
            "RequestLifetime must be at least one minute and not far beyond the token's lifetime.");
        Require(
            MinimumResponseTime >= TimeSpan.Zero && MinimumResponseTime <= TimeSpan.FromSeconds(5),
            "MinimumResponseTime must be between zero and five seconds.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"{SectionName}:{message}");
        }
    }
}
