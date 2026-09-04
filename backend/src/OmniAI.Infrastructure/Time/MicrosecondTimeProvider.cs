namespace OmniAI.Infrastructure.Time;

/// <summary>
/// A clock that reports instants at the precision the platform can durably
/// store.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DateTimeOffset"/> counts 100-nanosecond ticks. PostgreSQL's
/// <c>timestamptz</c> stores microseconds. Without this, an instant taken from
/// the system clock carries up to three digits that cannot survive being
/// written, and the platform ends up reporting a time it did not record.
/// </para>
/// <para>
/// That is not merely untidy. A create response is built from the aggregate in
/// memory -- nothing re-reads the row it just wrote -- so the client is handed
/// <c>2026-05-01T09:00:00.6905973Z</c> while the database holds
/// <c>...0.690597Z</c>. Read the same resource back a moment later and its
/// <c>createdAt</c> has apparently changed, having never been edited. Anything
/// that compares the two -- a cache, a diff, a "has this changed since I last
/// looked" check, an equality assertion in a test -- sees a difference that does
/// not exist. It is exactly the kind of inconsistency that is invisible until
/// something downstream depends on it.
/// </para>
/// <para>
/// Truncating at the source fixes it once, for every use case, in the only place
/// that knows the constraint exists. Truncation rather than rounding, so a
/// recorded instant is never later than the moment it describes.
/// </para>
/// <para>
/// It lives in Infrastructure because the limit is a storage property, not a
/// business rule: the Domain still takes whatever instant it is handed, the
/// Application still asks a <see cref="TimeProvider"/> for the time, and neither
/// knows this type exists. If the store ever changed to one with finer
/// resolution, this class is the only thing that would need to.
/// </para>
/// </remarks>
public sealed class MicrosecondTimeProvider(TimeProvider inner) : TimeProvider
{
    private readonly TimeProvider _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset now = _inner.GetUtcNow();

        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>
    /// Deliberately not truncated. This is a monotonic counter for measuring
    /// elapsed time, not a wall-clock instant, and nothing persists it -- so
    /// coarsening it would only make duration measurements worse.
    /// </summary>
    public override long GetTimestamp() => _inner.GetTimestamp();

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) =>
        _inner.CreateTimer(callback, state, dueTime, period);
}
