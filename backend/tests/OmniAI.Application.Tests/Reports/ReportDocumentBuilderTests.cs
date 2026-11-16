using OmniAI.Application.Analytics;
using OmniAI.Application.Reports;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Reports;

/// <summary>
/// What a report says, independent of the file it becomes.
/// </summary>
/// <remarks>
/// The builder defines no metric: every figure is copied from the analytics read
/// or the customer read. These tests prove the copying is faithful, the ordering
/// explicit, the bounds enforced and the currencies never added.
/// </remarks>
public sealed class ReportDocumentBuilderTests
{
    private static readonly TenantId Workspace = TenantId.From(Guid.NewGuid());
    private static readonly DateTimeOffset Generated = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReportPeriod September = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4));

    [Fact]
    public async Task Conversation_activity_copies_the_dashboard_figures_and_orders_days()
    {
        FakeAnalytics analytics = new()
        {
            Result = Analytics(daily:
            [
                new AnalyticsDay(new DateOnly(2026, 9, 3), 1, 2, 3, 4),
                new AnalyticsDay(new DateOnly(2026, 9, 1), 5, 6, 7, 8),
                new AnalyticsDay(new DateOnly(2026, 9, 2), 0, 0, 0, 0),
            ]),
        };

        ReportDocument document = await Build(analytics, ReportType.ConversationActivity);

        Assert.Equal("Conversation activity", document.Title);
        Assert.Equal(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 3)],
            document.Primary.Rows.Select(row => row[0].DateValue!.Value));
        Assert.Equal(6, Fact(document, "Conversations started"));
        Assert.Contains(document.Summary, fact => fact.Label.Contains("current snapshot", StringComparison.Ordinal));

        // The same window the dashboard's reader is asked for: whole days, half-open.
        Assert.Equal(September.From, analytics.LastWindow!.From);
        Assert.Equal(September.To, analytics.LastWindow.To);
    }

    [Theory]
    [InlineData(ReportType.ProviderUsage, UserLanguage.English, "OpenAi", "OpenAI")]
    [InlineData(ReportType.ProviderUsage, UserLanguage.English, "DevelopmentStandIn", "Local test stand-in")]
    [InlineData(ReportType.ProviderCost, UserLanguage.English, "DevelopmentStandIn", "Local test stand-in")]
    [InlineData(ReportType.ProviderUsage, UserLanguage.Arabic, "OpenAi", "OpenAI")]
    [InlineData(ReportType.ProviderCost, UserLanguage.Arabic, "DevelopmentStandIn", "البديل التجريبي المحلي")]
    [InlineData(ReportType.ProviderCost, UserLanguage.English, "SomeFutureProvider", "SomeFutureProvider")]
    public async Task Provider_files_name_each_provider_as_a_reader_knows_it(
        ReportType type, UserLanguage language, string code, string expected)
    {
        WorkspaceAnalytics read = Analytics() with
        {
            Models = [new AnalyticsModelUsage(code, "test-model", AiOperationType.TextGeneration, 3, 10, 5, null, null, 3)],
        };

        ReportDocument document = await new ReportDocumentBuilder(new FakeAnalytics { Result = read }, new FakeCustomers())
            .BuildAsync(Workspace, type, September, "Acme", Generated, 1000, language);

        ReportTable models = document.Tables[0];

        // The name, not the ledger's code (Phase 39.8a); a code it does not know
        // is printed as stored, so nothing is hidden.
        Assert.Equal(expected, models.Rows.Single()[0].TextValue);
        Assert.Equal("test-model", models.Rows.Single()[1].TextValue);
    }

    [Fact]
    public async Task Provider_cost_keeps_every_currency_separate()
    {
        FakeAnalytics analytics = new()
        {
            Result = Analytics(rated:
            [
                new AnalyticsRatedCost("USD", 1.25m, 3),
                new AnalyticsRatedCost("EUR", 2.50m, 4),
            ]),
        };

        ReportDocument document = await Build(analytics, ReportType.ProviderCost);

        ReportTable byCurrency = document.Tables.Single(table => table.Name == "By currency");

        Assert.Equal(["EUR", "USD"], byCurrency.Rows.Select(row => row[0].TextValue));
        Assert.Equal([2.50m, 1.25m], byCurrency.Rows.Select(row => row[1].MoneyValue!.Value));

        // No row, fact or note carries a single figure that adds the two.
        Assert.DoesNotContain(document.Summary, fact => fact.Value.MoneyValue == 3.75m);
        Assert.Contains(document.Notes, note => note.Contains("never added together", StringComparison.Ordinal));
        Assert.Contains(document.Notes, note => note.Contains("not billing", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Notes, note => note.Contains("invoice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(document.Notes, note => note.Contains("Actual cost", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Estimated provider cost", document.Title);
    }

    [Fact]
    public async Task Channel_usage_always_lists_the_implemented_channels()
    {
        FakeAnalytics analytics = new()
        {
            Result = Analytics(channels: [new AnalyticsChannel(ChannelKind.WhatsApp, 2, 5, 4, 1)]),
        };

        ReportDocument document = await Build(analytics, ReportType.ChannelUsage);

        Assert.Equal(["Website", "WhatsApp"], document.Primary.Rows.Select(row => row[0].TextValue));
        Assert.Equal(0, document.Primary.Rows[0][1].CountValue);
        Assert.Equal(2, document.Primary.Rows[1][1].CountValue);
    }

    [Fact]
    public async Task WhatsApp_delivery_lists_all_seven_states_even_when_nothing_was_sent()
    {
        ReportDocument document = await Build(new FakeAnalytics { Result = Analytics() }, ReportType.WhatsAppDelivery);

        Assert.Equal(
            ["Pending", "Sending", "Sent", "Delivered", "Read", "Failed", "Unknown"],
            document.Primary.Rows.Select(row => row[0].TextValue));
    }

    [Fact]
    public async Task Customer_activity_uses_the_directory_label_and_orders_by_last_activity_then_id()
    {
        Guid low = new("00000000-0000-0000-0000-000000000001");
        Guid high = new("00000000-0000-0000-0000-000000000002");
        DateTimeOffset at = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

        FakeCustomers customers = new()
        {
            Rows =
            [
                new CustomerActivityRow(CustomerId.From(low), null, [ChannelKind.WhatsApp], 1, 1, 2, at, at),
                new CustomerActivityRow(CustomerId.From(high), CustomerDisplayName.Create("=HYPERLINK(\"x\")"), [ChannelKind.WebChat], 1, 0, 1, at, at),
                new CustomerActivityRow(CustomerId.New(), null, [ChannelKind.WebChat, ChannelKind.WhatsApp], 3, 2, 9, at, at.AddHours(5)),
            ],
        };

        ReportDocument document = await Build(new FakeAnalytics { Result = Analytics() }, ReportType.CustomerActivity, customers);

        Assert.Equal(
            ["Unnamed customer", "=HYPERLINK(\"x\")", "WhatsApp customer"],
            document.Primary.Rows.Select(row => row[1].TextValue));
    }

    [Fact]
    public async Task A_report_over_the_row_bound_is_refused_not_truncated()
    {
        FakeCustomers customers = new()
        {
            Rows =
            [
                .. Enumerable.Range(0, 6).Select(_ => new CustomerActivityRow(
                    CustomerId.New(), null, [ChannelKind.WebChat], 1, 1, 1, Generated, Generated)),
            ],
        };

        await Assert.ThrowsAsync<ReportTooLargeException>(
            () => Build(new FakeAnalytics { Result = Analytics() }, ReportType.CustomerActivity, customers, rowLimit: 5));

        Assert.Equal(6, customers.LastLimit);
    }

    [Fact]
    public async Task Agent_breakdowns_are_read_one_past_the_bound()
    {
        FakeAnalytics analytics = new() { Result = Analytics() };

        await Build(analytics, ReportType.AgentActivity, rowLimit: 250);

        Assert.Equal(new AnalyticsBreakdown(251, 251), analytics.LastBreakdown);
    }

    [Fact]
    public void Every_report_requires_its_data_permission_and_none_requires_transcripts()
    {
        foreach (ReportDefinition definition in ReportCatalog.All)
        {
            Assert.NotEmpty(definition.DataPermissions);
            Assert.DoesNotContain(PlatformPermissions.ConversationsView, definition.DataPermissions);
            Assert.Contains(PlatformPermissions.ReportsGenerate, ReportCatalog.ToGenerate(definition.Type));
            Assert.Contains(PlatformPermissions.ReportsView, ReportCatalog.ToDownload(definition.Type));
            Assert.DoesNotContain(PlatformPermissions.ReportsGenerate, ReportCatalog.ToDownload(definition.Type));
        }

        Assert.Equal([PlatformPermissions.CustomersView], ReportCatalog.For(ReportType.CustomerActivity).DataPermissions);
        Assert.Equal([PlatformPermissions.AnalyticsView], ReportCatalog.For(ReportType.ProviderCost).DataPermissions);
        Assert.Equal(8, ReportCatalog.All.Count);
    }

    [Fact]
    public void A_download_name_is_built_only_from_server_parts()
    {
        string name = ReportCatalog.FileName(
            ReportType.ConversationActivity,
            ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)),
            ReportFormat.Xlsx);

        Assert.Equal("conversation-activity-2026-09-01-to-2026-09-30.xlsx", name);
    }

    // ------------------------------------------------------------- time zones

    [Fact]
    public async Task A_utc_report_says_utc_exactly_as_before()
    {
        FakeAnalytics analytics = new() { Result = Analytics() };

        ReportDocument document = await Build(analytics, ReportType.ConversationActivity);

        Assert.Equal(WorkspaceTimeZone.Utc, analytics.LastWindow!.TimeZone);
        Assert.Equal("Date (UTC)", document.Tables[0].Columns[0].Header);
        Assert.Equal(
            "Period: 2026-09-01 to 2026-09-03 inclusive, in UTC whole days.",
            document.Notes[0]);
    }

    /// <remarks>Phase 37.6: the report's own zone reaches the read and every label.</remarks>
    [Fact]
    public async Task A_gulf_report_reads_gulf_days_and_says_so()
    {
        WorkspaceTimeZone riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4), riyadh);
        FakeAnalytics analytics = new() { Result = Analytics() };

        ReportDocument document = await new ReportDocumentBuilder(analytics, new FakeCustomers())
            .BuildAsync(Workspace, ReportType.ConversationActivity, period, "Acme", Generated, 1000);

        Assert.Equal(AnalyticsWindow.ForDays(period.FromDate, period.ToExclusiveDate, riyadh), analytics.LastWindow);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero), analytics.LastWindow!.From);
        Assert.Equal("Date (Asia/Riyadh)", document.Tables[0].Columns[0].Header);
        Assert.Equal(
            "Period: 2026-11-01 to 2026-11-03 inclusive, in Asia/Riyadh whole days.",
            document.Notes[0]);
        Assert.Equal(riyadh, document.Period.TimeZone);
    }

    // ---------------------------------------------------------------- helpers

    private static Task<ReportDocument> Build(
        FakeAnalytics analytics,
        ReportType type,
        FakeCustomers? customers = null,
        int rowLimit = 1000) =>
        new ReportDocumentBuilder(analytics, customers ?? new FakeCustomers())
            .BuildAsync(Workspace, type, September, "Acme", Generated, rowLimit);

    private static long Fact(ReportDocument document, string label) =>
        document.Summary.Single(fact => fact.Label == label).Value.CountValue!.Value;

    private static WorkspaceAnalytics Analytics(
        IReadOnlyList<AnalyticsDay>? daily = null,
        IReadOnlyList<AnalyticsChannel>? channels = null,
        IReadOnlyList<AnalyticsRatedCost>? rated = null) =>
        new(
            new AnalyticsPeriod("custom", September.From, September.To, 3, "UTC"),
            new AnalyticsSummary((daily ?? []).Sum(day => day.ConversationsStarted), 0, 0, 0, 0),
            new AnalyticsSnapshot(0, 0, 0),
            daily ?? [],
            channels ?? [],
            [],
            new AnalyticsAiOperations(0, 0, 0, 0),
            WhatsAppDelivery: null,
            new AnalyticsAiUsage(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, rated ?? [], []),
            [new AnalyticsModelUsage("OpenAi", "test-model", AiOperationType.TextGeneration, 3, 10, 5, null, null, 0)],
            FirstActivityOn: null);

    private sealed class FakeAnalytics : IWorkspaceAnalyticsReader
    {
        public WorkspaceAnalytics Result { get; init; } = null!;

        public AnalyticsWindow? LastWindow { get; private set; }

        public AnalyticsBreakdown? LastBreakdown { get; private set; }

        public Task<AnalyticsComparison> ReadComparisonAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A report never compares periods.");

        public Task<WorkspaceAnalytics> ReadAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            AnalyticsBreakdown breakdown,
            CancellationToken cancellationToken = default)
        {
            LastWindow = window;
            LastBreakdown = breakdown;

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCustomers : ICustomerActivityReader
    {
        public IReadOnlyList<CustomerActivityRow> Rows { get; init; } = [];

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<CustomerActivityRow>> ReadAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;

            return Task.FromResult<IReadOnlyList<CustomerActivityRow>>([.. Rows.Take(limit)]);
        }
    }
}
