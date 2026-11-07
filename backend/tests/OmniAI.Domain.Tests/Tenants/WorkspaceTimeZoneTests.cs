using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Tenants;

/// <summary>
/// The workspace time zone (Phase 37.6, row 34): which names it accepts, and how
/// it turns instants into local days and local days into instants.
/// </summary>
/// <remarks>
/// These run against the runtime's own zone database (tzdata in the Linux test
/// image), which is what production uses too.
/// </remarks>
public sealed class WorkspaceTimeZoneTests
{
    private static readonly WorkspaceTimeZone Riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");
    private static readonly WorkspaceTimeZone Berlin = WorkspaceTimeZone.Create("Europe/Berlin");

    // ------------------------------------------------------------------ names

    [Fact]
    public void Every_workspace_starts_in_utc()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        Assert.Equal(WorkspaceTimeZone.Utc, tenant.TimeZone);
        Assert.Equal("UTC", tenant.TimeZone.Name);
        Assert.True(tenant.TimeZone.IsUtc);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Riyadh")]
    [InlineData("Asia/Dubai")]
    [InlineData("Europe/Berlin")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Asia/Kathmandu")]
    public void An_iana_name_the_runtime_knows_is_accepted_as_spelt(string name)
    {
        WorkspaceTimeZone zone = WorkspaceTimeZone.Create($"  {name}  ");

        Assert.Equal(name, zone.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("asia/riyadh")]
    [InlineData("ASIA/RIYADH")]
    [InlineData("+03:00")]
    [InlineData("GMT+3")]
    [InlineData("Arab Standard Time")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("posix/Asia/Riyadh")]
    [InlineData("right/Asia/Riyadh")]
    [InlineData("Asia/ Riyadh")]
    [InlineData("../../etc/passwd")]
    public void Anything_else_is_refused_with_one_code(string? name)
    {
        DomainValidationException refusal =
            Assert.Throws<DomainValidationException>(() => WorkspaceTimeZone.Create(name));

        Assert.Equal("workspace.time_zone.invalid", refusal.Code);
    }

    [Fact]
    public void A_name_longer_than_the_column_is_refused()
    {
        string name = "Asia/" + new string('A', WorkspaceTimeZone.MaxLength);

        Assert.Equal(
            "workspace.time_zone.invalid",
            Assert.Throws<DomainValidationException>(() => WorkspaceTimeZone.Create(name)).Code);
    }

    [Fact]
    public void Zones_are_equal_by_name()
    {
        Assert.Equal(WorkspaceTimeZone.Create("Asia/Riyadh"), WorkspaceTimeZone.FromStored("Asia/Riyadh"));
        Assert.NotEqual(Riyadh, Berlin);
        Assert.Same(WorkspaceTimeZone.Utc, WorkspaceTimeZone.FromStored("UTC"));
    }

    [Fact]
    public void A_stored_name_is_read_without_its_rules_and_fails_loudly_only_when_used()
    {
        WorkspaceTimeZone gone = WorkspaceTimeZone.FromStored("Mars/Olympus_Mons");

        Assert.Equal("Mars/Olympus_Mons", gone.Name);
        Assert.Throws<InvalidOperationException>(() => gone.DateOf(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Changing_a_workspace_zone_says_whether_anything_changed()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        Assert.False(tenant.ChangeTimeZone(WorkspaceTimeZone.Utc));
        Assert.True(tenant.ChangeTimeZone(Riyadh));
        Assert.Equal(Riyadh, tenant.TimeZone);
        Assert.False(tenant.ChangeTimeZone(WorkspaceTimeZone.Create("Asia/Riyadh")));
    }

    // ------------------------------------------------------------------- days

    [Fact]
    public void Half_past_ten_at_night_utc_is_the_next_day_in_the_gulf()
    {
        DateTimeOffset instant = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, 3), WorkspaceTimeZone.Utc.DateOf(instant));
        Assert.Equal(new DateOnly(2026, 11, 4), Riyadh.DateOf(instant));
    }

    [Fact]
    public void A_gulf_day_starts_at_nine_in_the_evening_utc()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 11, 3, 21, 0, 0, TimeSpan.Zero),
            Riyadh.StartOf(new DateOnly(2026, 11, 4)));

        Assert.Equal(TimeSpan.Zero, Riyadh.StartOf(new DateOnly(2026, 11, 4)).Offset);
    }

    [Fact]
    public void A_utc_day_is_its_own_midnight()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero),
            WorkspaceTimeZone.Utc.StartOf(new DateOnly(2026, 11, 4)));
    }

    [Fact]
    public void The_day_berlin_clocks_go_back_is_twenty_five_hours_long()
    {
        DateTimeOffset start = Berlin.StartOf(new DateOnly(2026, 10, 25));
        DateTimeOffset end = Berlin.StartOf(new DateOnly(2026, 10, 26));

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), end);
        Assert.Equal(TimeSpan.FromHours(25), end - start);
    }

    [Fact]
    public void The_day_berlin_clocks_go_forward_is_twenty_three_hours_long()
    {
        DateTimeOffset start = Berlin.StartOf(new DateOnly(2026, 3, 29));
        DateTimeOffset end = Berlin.StartOf(new DateOnly(2026, 3, 30));

        Assert.Equal(TimeSpan.FromHours(23), end - start);
    }

    /// <remarks>
    /// The property every reader relies on: the start of a local day is on that
    /// day, and the instant before it is on the day before. Checked for every day
    /// of 2026 in zones with half-hour and 45-minute offsets, and in zones whose
    /// clocks change <em>at</em> midnight, where local midnight is skipped (Santiago
    /// in September, Beirut in March) or happens twice (Havana in November).
    /// </remarks>
    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Riyadh")]
    [InlineData("Europe/Berlin")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Asia/Kathmandu")]
    [InlineData("America/St_Johns")]
    [InlineData("Australia/Lord_Howe")]
    [InlineData("America/Santiago")]
    [InlineData("Asia/Beirut")]
    [InlineData("America/Havana")]
    public void Every_local_day_of_2026_starts_on_itself_and_follows_the_day_before(string name)
    {
        WorkspaceTimeZone zone = WorkspaceTimeZone.Create(name);

        for (DateOnly day = new(2026, 1, 1); day < new DateOnly(2027, 1, 1); day = day.AddDays(1))
        {
            DateTimeOffset start = zone.StartOf(day);

            Assert.Equal(day, zone.DateOf(start));
            Assert.Equal(day.AddDays(-1), zone.DateOf(start.AddTicks(-1)));

            TimeSpan length = zone.StartOf(day.AddDays(1)) - start;
            Assert.InRange(length, TimeSpan.FromHours(22), TimeSpan.FromHours(26));
        }
    }

    [Fact]
    public void Zones_whose_clocks_change_at_midnight_are_exercised_by_the_test_above()
    {
        // If tzdata ever moved these transitions off midnight, the property test
        // would stop covering the skipped and repeated midnight; this says so.
        TimeZoneInfo santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        TimeZoneInfo havana = TimeZoneInfo.FindSystemTimeZoneById("America/Havana");

        IEnumerable<DateTime> midnights = Enumerable
            .Range(0, 365)
            .Select(offset => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(offset));

        Assert.Contains(midnights, santiago.IsInvalidTime);
        Assert.Contains(midnights, havana.IsAmbiguousTime);
    }
}
