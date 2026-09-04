namespace OmniAI.Testing;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it.
/// </summary>
/// <remarks>
/// Written out rather than taken from a testing package. It is a subclass with
/// one overridden method, which is smaller than any dependency that could supply
/// it, and no NuGet package is added to the solution for it.
/// <para>
/// Deterministic time is what makes the timestamp assertions in these suites
/// exact rather than approximate. A test can state that <c>createdAt</c> is
/// precisely <c>2026-05-01T09:00:00Z</c>, and -- more importantly -- it can move
/// the clock <em>backwards</em> to prove the accepted high-water-mark rule:
/// an out-of-order instant still applies the edit but must not drag
/// <c>updatedAt</c> back with it. A real clock cannot express that at all.
/// </para>
/// <para>
/// Compiled into the test projects that need it from <c>backend/tests/Shared</c>,
/// following the convention already used for <c>AssemblyReferenceReader</c> and
/// <c>PostgresTestDatabase</c>.
/// </para>
/// </remarks>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>The instant this provider currently reports.</summary>
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves the clock and returns this provider, for chaining.</summary>
    /// <remarks>
    /// A negative <paramref name="by"/> is allowed on purpose: rewinding is how
    /// the high-water-mark tests produce an out-of-order instant.
    /// </remarks>
    public FixedTimeProvider Advance(TimeSpan by)
    {
        Now = Now.Add(by);

        return this;
    }
}
