using OmniAI.Application.Analytics;
using OmniAI.Application.Reports;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// One fixed workspace's figures, for building every report type from the real
/// builder without a database (Phase 37.7).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fixed on purpose.</strong> Every id, name, figure and instant is a
/// constant, so a report built from these is the same file on every run. The
/// English golden files were captured from these figures before Phase 37.7
/// changed anything.
/// </para>
/// <para>
/// <strong>Every branch the builder words.</strong> Both channels and one more,
/// both operations, two currencies and an unrated model, all four customer labels
/// (named, unnamed, WhatsApp, visitor), names in Arabic and Latin, an e-mail-like
/// name and a hostile one, and every WhatsApp state.
/// </para>
/// </remarks>
internal static class ReportFixtures
{
    public const string WorkspaceName = "Northwind Trading";

    public static readonly DateTimeOffset GeneratedAt = new(2026, 11, 7, 9, 30, 15, TimeSpan.Zero);

    public static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8));

    private static readonly TenantId Workspace = TenantId.From(new Guid("7d2a9a52-8d11-4b3c-9a51-2f0e7c1b6a01"));

    public static IReportRenderer Renderer(ReportFormat format) => format switch
    {
        ReportFormat.Csv => new Infrastructure.Reports.Rendering.CsvReportRenderer(),
        ReportFormat.Xlsx => new Infrastructure.Reports.Rendering.XlsxReportRenderer(),
        ReportFormat.Pdf => new Infrastructure.Reports.Rendering.PdfReportRenderer(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static Task<ReportDocument> BuildAsync(
        ReportType type,
        ReportPeriod? period = null,
        UserLanguage language = UserLanguage.English) =>
        new ReportDocumentBuilder(new Analytics(), new Customers())
            .BuildAsync(Workspace, type, period ?? Period, WorkspaceName, GeneratedAt, 2_000, language);

    public static WorkspaceAnalytics Figures(ReportPeriod period) =>
        new(
            new AnalyticsPeriod("custom", period.From, period.To, 7, period.TimeZone.Name),
            new AnalyticsSummary(42, 318, 266, 37, 9),
            new AnalyticsSnapshot(12, 8, 4),
            [
                new AnalyticsDay(new DateOnly(2026, 10, 3), 7, 51, 44, 6),
                new AnalyticsDay(new DateOnly(2026, 10, 1), 5, 40, 33, 4),
                new AnalyticsDay(new DateOnly(2026, 10, 2), 0, 0, 0, 0),
                new AnalyticsDay(new DateOnly(2026, 10, 4), 12, 96, 81, 11),
                new AnalyticsDay(new DateOnly(2026, 10, 5), 4, 30, 25, 3),
                new AnalyticsDay(new DateOnly(2026, 10, 6), 9, 61, 50, 8),
                new AnalyticsDay(new DateOnly(2026, 10, 7), 5, 40, 33, 5),
            ],
            [
                new AnalyticsChannel(ChannelKind.WhatsApp, 17, 140, 118, 15),
                new AnalyticsChannel(ChannelKind.WebChat, 24, 172, 143, 22),
                new AnalyticsChannel(ChannelKind.Instagram, 1, 6, 5, 0),
            ],
            [
                new AnalyticsAgent(new Guid("0b7c3f2e-1111-4a0e-8b8b-000000000001"), "مساعد المبيعات", 20, 150, 130, 12),
                new AnalyticsAgent(new Guid("0b7c3f2e-1111-4a0e-8b8b-000000000002"), "Support Bot <beta>", 15, 120, 100, 20),
                new AnalyticsAgent(new Guid("0b7c3f2e-1111-4a0e-8b8b-000000000003"), "=HYPERLINK(\"x\")", 7, 48, 36, 5),
            ],
            new AnalyticsAiOperations(280, 266, 9, 5),
            new AnalyticsWhatsAppDelivery(1, 2, 30, 61, 20, 3, 1),
            new AnalyticsAiUsage(
                Calls: 410,
                GenerationCalls: 280,
                EmbeddingCalls: 130,
                Succeeded: 398,
                Failed: 9,
                Unresolved: 3,
                GenerationInputTokens: 512_340,
                GenerationCachedInputTokens: 120_000,
                GenerationOutputTokens: 86_002,
                GenerationReasoningTokens: 4_100,
                EmbeddingInputTokens: 64_000,
                UnratedCalls: 6,
                RatedCost: [new AnalyticsRatedCost("USD", 1.234567m, 300), new AnalyticsRatedCost("EUR", 0.5m, 104)],
                ByPurpose: []),
            [
                new AnalyticsModelUsage("OpenAi", "gpt-4.1-mini", AiOperationType.TextGeneration, 280, 512_340, 86_002, 1.234567m, "USD", 0),
                new AnalyticsModelUsage("OpenAi", "text-embedding-3-small", AiOperationType.Embedding, 124, 60_000, 0, 0.5m, "EUR", 0),
                new AnalyticsModelUsage("DevelopmentStandIn", "local-stand-in", AiOperationType.Embedding, 6, 4_000, 0, null, null, 6),
            ],
            FirstActivityOn: new DateOnly(2026, 9, 14));

    public static IReadOnlyList<CustomerActivityRow> CustomerRows()
    {
        DateTimeOffset at = new(2026, 10, 4, 8, 15, 0, TimeSpan.Zero);

        return
        [
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000001")),
                CustomerDisplayName.Create("أحمد محمد"),
                [ChannelKind.WhatsApp],
                2, 1, 14, at, at.AddHours(30)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000002")),
                CustomerDisplayName.Create("José García"),
                [ChannelKind.WebChat, ChannelKind.WhatsApp],
                1, 1, 6, at.AddHours(-20), at.AddHours(2)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000003")),
                CustomerDisplayName.Create("lina@example.com"),
                [ChannelKind.WebChat],
                1, 0, 3, at.AddHours(-5), at.AddHours(-1)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000004")),
                null,
                [ChannelKind.WebChat, ChannelKind.WhatsApp],
                3, 2, 9, at.AddDays(-2), at.AddHours(5)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000005")),
                null,
                [ChannelKind.WhatsApp],
                1, 1, 2, at.AddDays(-1), at.AddDays(-1)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000006")),
                null,
                [ChannelKind.WebChat],
                1, 1, 1, at.AddDays(-3), at.AddDays(-3)),
            new CustomerActivityRow(
                CustomerId.From(new Guid("5f0d1e2c-0000-4000-8000-000000000007")),
                CustomerDisplayName.Create("=SUM(A1:A2) شركة النور"),
                [ChannelKind.WebChat],
                1, 1, 4, at.AddHours(-3), at.AddHours(-2)),
        ];
    }

    private sealed class Analytics : IWorkspaceAnalyticsReader
    {
        public Task<WorkspaceAnalytics> ReadAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            AnalyticsBreakdown breakdown,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Figures(ReportPeriod.FromInstants(window.From, window.To, window.TimeZone)));

        public Task<AnalyticsComparison> ReadComparisonAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A report never compares periods.");
    }

    private sealed class Customers : ICustomerActivityReader
    {
        public Task<IReadOnlyList<CustomerActivityRow>> ReadAsync(
            TenantId tenantId,
            AnalyticsWindow window,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerActivityRow>>([.. CustomerRows().Take(limit)]);
    }
}
