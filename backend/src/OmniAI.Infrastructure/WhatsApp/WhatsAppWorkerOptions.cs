namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// How the two WhatsApp workers pace themselves.
/// </summary>
/// <remarks>
/// Deployment settings, bound from <c>WhatsApp:Workers</c>. None of them changes
/// what the platform does — only how quickly and how often it tries.
/// </remarks>
internal sealed class WhatsAppWorkerOptions
{
    /// <summary>Configuration section these values are bound from.</summary>
    public const string SectionName = "WhatsApp:Workers";

    /// <summary>
    /// How long a worker waits after finding nothing to do.
    /// </summary>
    /// <remarks>
    /// Polling rather than listening, because the alternative — a notification
    /// channel — adds a second delivery mechanism that can itself be missed, and
    /// then a poll has to exist anyway as the thing that catches what was missed.
    /// With one mechanism there is one thing to reason about. A worker that found
    /// work does not wait at all, so a busy queue drains at the speed of the work
    /// rather than at the speed of this interval.
    /// </remarks>
    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How many rows one pass claims.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// How many times one message may be attempted before it is given up on.
    /// </summary>
    /// <remarks>
    /// Applies to inbound processing only. A send is never re-attempted at all:
    /// the first attempt may have reached the customer.
    /// </remarks>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long a claim may stand before the row is treated as abandoned.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. It must exceed the slowest legitimate turn — a long
    /// retrieval and a slow model — or a worker that is merely being thorough has
    /// its message taken away and processed twice. Its only job is to recover
    /// from a process that died, which is rare and not urgent.
    /// </remarks>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often abandoned claims are swept up.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Whether these settings can be used.</summary>
    public void Validate()
    {
        if (IdleInterval <= TimeSpan.Zero || IdleInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"{SectionName}:IdleInterval must be between zero and five minutes.");
        }

        if (BatchSize is < 1 or > 500)
        {
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and 500.");
        }

        if (MaxAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxAttempts must be between 1 and 100.");
        }

        if (ClaimTimeout < TimeSpan.FromMinutes(1))
        {
            // Below a minute this stops being crash recovery and starts taking
            // work away from workers that are simply still busy.
            throw new InvalidOperationException(
                $"{SectionName}:ClaimTimeout must be at least one minute.");
        }

        if (SweepInterval <= TimeSpan.Zero || SweepInterval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                $"{SectionName}:SweepInterval must be between zero and one hour.");
        }
    }
}
