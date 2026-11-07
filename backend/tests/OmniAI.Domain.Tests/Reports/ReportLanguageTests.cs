using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Reports;

/// <summary>
/// Which language a report's file is written in (Phase 37.7, D-6, M9 and M10).
/// </summary>
public sealed class ReportLanguageTests
{
    private static readonly TenantId Workspace = TenantId.From(Guid.NewGuid());
    private static readonly UserId Member = UserId.From(Guid.NewGuid());
    private static readonly UserId Other = UserId.From(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReportPeriod October = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));

    [Fact]
    public void A_request_is_english_unless_it_says_otherwise()
    {
        Assert.Equal(UserLanguage.English, Request().Language);
        Assert.Equal(UserLanguage.Arabic, Request(UserLanguage.Arabic).Language);
    }

    [Fact]
    public void A_language_outside_the_two_is_refused() =>
        Assert.Throws<DomainValidationException>(() => Request((UserLanguage)99));

    [Fact]
    public void A_repeat_that_names_no_language_asks_for_the_report_already_settled()
    {
        ReportExecution arabic = Request(UserLanguage.Arabic);

        Assert.True(arabic.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Pdf, October));
        Assert.True(arabic.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Pdf, October, UserLanguage.Arabic));
        Assert.False(arabic.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Pdf, October, UserLanguage.English));
    }

    [Fact]
    public void A_schedule_keeps_the_language_it_was_created_with()
    {
        ReportSchedule schedule = Schedule(UserLanguage.Arabic);

        Assert.Equal(UserLanguage.Arabic, schedule.Language);
        Assert.Equal(UserLanguage.English, Schedule().Language);
    }

    [Fact]
    public void Saving_a_schedule_without_a_language_keeps_it_even_when_another_member_saves_it()
    {
        ReportSchedule schedule = Schedule(UserLanguage.Arabic);

        schedule.Update("Renamed", ReportType.AgentActivity, ReportFormat.Xlsx, ReportFrequency.Monthly, true, Other, Now.AddHours(1));
        schedule.SetEnabled(false, Other, Now.AddHours(2));
        schedule.SetEnabled(true, Member, Now.AddHours(3));

        Assert.Equal(UserLanguage.Arabic, schedule.Language);
        Assert.Equal(Member, schedule.OwnerUserId);
    }

    [Fact]
    public void An_edit_that_names_a_language_changes_it()
    {
        ReportSchedule schedule = Schedule(UserLanguage.Arabic);

        schedule.Update("Same", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Weekly, true, Member, Now, language: UserLanguage.English);

        Assert.Equal(UserLanguage.English, schedule.Language);

        Assert.Throws<DomainValidationException>(() => schedule.Update(
            "Same", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Weekly, true, Member, Now, language: (UserLanguage)7));
        Assert.Equal(UserLanguage.English, schedule.Language);
    }

    [Fact]
    public void Every_occurrence_is_written_in_its_schedule_s_language_not_its_owner_s()
    {
        ReportSchedule arabic = Schedule(UserLanguage.Arabic);
        ReportSchedule english = Schedule(UserLanguage.English);

        Assert.Equal(UserLanguage.Arabic, ReportExecution.ForSchedule(arabic, October, Now).Language);
        Assert.Equal(UserLanguage.English, ReportExecution.ForSchedule(english, October, Now).Language);
    }

    private static ReportExecution Request(UserLanguage? language = null) =>
        language is UserLanguage chosen
            ? ReportExecution.Request(Workspace, ReportType.ChannelUsage, ReportFormat.Pdf, October, Member, Guid.NewGuid(), Now, chosen)
            : ReportExecution.Request(Workspace, ReportType.ChannelUsage, ReportFormat.Pdf, October, Member, Guid.NewGuid(), Now);

    private static ReportSchedule Schedule(UserLanguage? language = null) =>
        language is UserLanguage chosen
            ? ReportSchedule.Create(Workspace, "Channels", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Weekly, true, Member, Now, language: chosen)
            : ReportSchedule.Create(Workspace, "Channels", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Weekly, true, Member, Now);
}
