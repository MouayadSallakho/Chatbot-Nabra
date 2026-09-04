using OmniAI.Infrastructure.Time;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Time;

/// <summary>
/// The clock that keeps reported instants storable.
/// </summary>
/// <remarks>
/// Small, but load-bearing: it is what stops a create response reporting a
/// <c>createdAt</c> that differs from the one the very next read returns. These
/// tests need no database -- the behaviour is arithmetic -- but they belong in
/// this project because the reason the behaviour exists is PostgreSQL's
/// <c>timestamptz</c> resolution.
/// </remarks>
public sealed class MicrosecondTimeProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(1237)]
    [InlineData(9_999_999)]
    public void The_reported_instant_always_lands_on_a_whole_microsecond(long extraTicks)
    {
        MicrosecondTimeProvider clock = new(new FixedTimeProvider(Start.AddTicks(extraTicks)));

        Assert.Equal(0, clock.GetUtcNow().Ticks % TimeSpan.TicksPerMicrosecond);
    }

    [Fact]
    public void An_instant_already_on_a_microsecond_is_unchanged()
    {
        MicrosecondTimeProvider clock = new(new FixedTimeProvider(Start));

        Assert.Equal(Start, clock.GetUtcNow());
    }

    [Fact]
    public void Sub_microsecond_ticks_are_truncated_rather_than_rounded()
    {
        // Truncation, so a recorded instant is never later than the moment it
        // describes. Rounding 1237 ticks up would report 1240.
        MicrosecondTimeProvider clock = new(new FixedTimeProvider(Start.AddTicks(1237)));

        Assert.Equal(Start.AddTicks(1230), clock.GetUtcNow());
    }

    [Fact]
    public void Truncation_never_moves_an_instant_forwards()
    {
        FixedTimeProvider inner = new(Start.AddTicks(9));
        MicrosecondTimeProvider clock = new(inner);

        Assert.True(clock.GetUtcNow() <= inner.GetUtcNow());
    }

    [Fact]
    public void The_offset_is_preserved()
    {
        DateTimeOffset withOffset = new(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(3));
        MicrosecondTimeProvider clock = new(new FixedTimeProvider(withOffset.AddTicks(5)));

        Assert.Equal(withOffset.Offset, clock.GetUtcNow().Offset);
    }

    [Fact]
    public void The_clock_still_moves_when_the_inner_clock_moves()
    {
        // Truncation must not freeze time.
        FixedTimeProvider inner = new(Start);
        MicrosecondTimeProvider clock = new(inner);

        DateTimeOffset before = clock.GetUtcNow();
        inner.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(before.AddSeconds(30), clock.GetUtcNow());
    }

    [Fact]
    public void The_elapsed_time_counter_is_delegated_untouched()
    {
        // GetTimestamp measures durations and is never persisted, so coarsening it
        // would only make measurements worse.
        MicrosecondTimeProvider clock = new(TimeProvider.System);

        Assert.Equal(TimeProvider.System.TimestampFrequency, clock.TimestampFrequency);
        Assert.NotEqual(0, clock.GetTimestamp());
    }

    [Fact]
    public void The_local_time_zone_is_delegated()
    {
        MicrosecondTimeProvider clock = new(TimeProvider.System);

        Assert.Equal(TimeProvider.System.LocalTimeZone, clock.LocalTimeZone);
    }

    [Fact]
    public void A_null_inner_clock_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MicrosecondTimeProvider(null!));
    }
}
