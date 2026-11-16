using System.Globalization;
using OmniAI.Application.Analytics;
using OmniAI.Application.Customers;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>
/// Turns authoritative reads into a report document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It defines no metric.</strong> Every number here is a field of
/// <see cref="WorkspaceAnalytics"/>, read through the same reader the dashboard
/// uses, or a field of <see cref="CustomerActivityRow"/>, counted over the same
/// base sets. This class chooses columns and labels; it never counts.
/// </para>
/// <para>
/// <strong>It words nothing itself (Phase 37.7).</strong> Every phrase goes through
/// <see cref="ReportWording"/>, keyed by its English, so an English report says
/// exactly what it said before and an Arabic one says it in Arabic.
/// </para>
/// <para>
/// <strong>It spends nothing.</strong> No provider is called to write a summary,
/// explain a trend or suggest anything. A report is the stored numbers, laid out.
/// </para>
/// <para>
/// <strong>Ordering is explicit everywhere.</strong> Days ascending, channels in
/// their fixed order, agents and models by volume with stable tie-breakers,
/// customers by last activity then id. Nothing relies on the order a database
/// happened to return rows in.
/// </para>
/// </remarks>
public sealed class ReportDocumentBuilder(
    IWorkspaceAnalyticsReader analytics,
    ICustomerActivityReader customers)
{
    private const string NoText = "Counts and safe labels only. This report contains no message text.";

    private readonly IWorkspaceAnalyticsReader _analytics =
        analytics ?? throw new ArgumentNullException(nameof(analytics));

    private readonly ICustomerActivityReader _customers =
        customers ?? throw new ArgumentNullException(nameof(customers));

    /// <param name="language">
    /// The language the report is worded in (Phase 37.7): its title, labels, headings,
    /// notes and the platform's own cell values. Figures, dates and stored names are
    /// the same in every language.
    /// </param>
    /// <exception cref="ReportTooLargeException">The report would carry more than <paramref name="rowLimit"/> rows.</exception>
    public async Task<ReportDocument> BuildAsync(
        TenantId tenantId,
        ReportType type,
        ReportPeriod period,
        string? workspaceName,
        DateTimeOffset generatedAt,
        int rowLimit,
        UserLanguage language = UserLanguage.English,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowLimit);

        ReportWording w = ReportWording.For(language);

        // The report's own zone, kept on its execution (Phase 37.6): the same local
        // days the dashboard would count in that zone.
        AnalyticsWindow window = AnalyticsWindow.ForDays(period.FromDate, period.ToExclusiveDate, period.TimeZone);

        // One more than the bound, so "exactly at the limit" and "over it" can be
        // told apart without a second query. Nothing is silently truncated.
        int probe = rowLimit + 1;

        ReportDocument document;

        if (type == ReportType.CustomerActivity)
        {
            IReadOnlyList<CustomerActivityRow> rows = await _customers
                .ReadAsync(tenantId, window, probe, cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count > rowLimit)
            {
                throw new ReportTooLargeException();
            }

            document = CustomerActivity(rows, period, workspaceName, generatedAt, w);
        }
        else
        {
            WorkspaceAnalytics read = await _analytics
                .ReadAsync(tenantId, window, new AnalyticsBreakdown(probe, probe), cancellationToken)
                .ConfigureAwait(false);

            if (read.Agents.Count > rowLimit || read.Models.Count > rowLimit)
            {
                throw new ReportTooLargeException();
            }

            document = type switch
            {
                ReportType.ConversationActivity => ConversationActivity(read, period, workspaceName, generatedAt, w),
                ReportType.ChannelUsage => ChannelUsage(read, period, workspaceName, generatedAt, w),
                ReportType.AiHumanHandling => AiHumanHandling(read, period, workspaceName, generatedAt, w),
                ReportType.AgentActivity => AgentActivity(read, period, workspaceName, generatedAt, w),
                ReportType.ProviderUsage => ProviderUsage(read, period, workspaceName, generatedAt, w),
                ReportType.ProviderCost => ProviderCost(read, period, workspaceName, generatedAt, w),
                ReportType.WhatsAppDelivery => WhatsAppDelivery(read, period, workspaceName, generatedAt, w),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        if (document.RowCount > rowLimit)
        {
            throw new ReportTooLargeException();
        }

        return document;
    }

    // ------------------------------------------------------------- the reports

    private static ReportDocument ConversationActivity(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        AnalyticsSummary summary = read.Summary;
        AnalyticsSnapshot now = read.Current;

        return Document(
            ReportType.ConversationActivity,
            period,
            workspace,
            at,
            w,
            [
                Fact(w["Conversations started"], summary.ConversationsStarted),
                Fact(w["Customer messages"], summary.CustomerMessages),
                Fact(w["AI messages"], summary.AiMessages),
                Fact(w["Human messages"], summary.HumanMessages),
                Fact(w["System messages"], summary.SystemMessages),
                Fact(w["Open conversations (current snapshot)"], now.OpenConversations),
                Fact(w["Open, handled by AI (current snapshot)"], now.OpenHandledByAi),
                Fact(w["Open, handled by a person (current snapshot)"], now.OpenHandledByHuman),
            ],
            [
                Table(
                    w["Daily activity"],
                    w["Daily activity"],
                    [
                        Column(w.DateHeader(period.TimeZone), ReportValueKind.Date),
                        Column(w["Conversations started"], ReportValueKind.Count),
                        Column(w["Customer messages"], ReportValueKind.Count),
                        Column(w["AI messages"], ReportValueKind.Count),
                        Column(w["Human messages"], ReportValueKind.Count),
                    ],
                    read.DailyActivity
                        .OrderBy(day => day.Date)
                        .Select(day => Row(
                            ReportValue.Date(day.Date),
                            ReportValue.Count(day.ConversationsStarted),
                            ReportValue.Count(day.CustomerMessages),
                            ReportValue.Count(day.AiMessages),
                            ReportValue.Count(day.HumanMessages)))),
            ],
            [
                w["Snapshot figures describe the workspace at the moment the report was generated, not the period."],
                w[NoText],
            ]);
    }

    private static ReportDocument ChannelUsage(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        // Website and WhatsApp always appear, used or not: they are the channels
        // this platform implements. Anything else appears only if it was used.
        IEnumerable<ChannelKind> channels = new[] { ChannelKind.WebChat, ChannelKind.WhatsApp }
            .Concat(read.Channels.Select(channel => channel.Channel))
            .Distinct()
            .OrderBy(channel => channel);

        List<IReadOnlyList<ReportValue>> rows =
        [
            .. channels.Select(kind =>
            {
                AnalyticsChannel? row = read.Channels.FirstOrDefault(channel => channel.Channel == kind);

                return Row(
                    ReportValue.Text(w.Channel(kind)),
                    ReportValue.Count(row?.ConversationsStarted ?? 0),
                    ReportValue.Count(row?.CustomerMessages ?? 0),
                    ReportValue.Count(row?.AiMessages ?? 0),
                    ReportValue.Count(row?.HumanMessages ?? 0));
            }),
        ];

        return Document(
            ReportType.ChannelUsage,
            period,
            workspace,
            at,
            w,
            [
                Fact(w["Conversations started"], read.Summary.ConversationsStarted),
                Fact(w["Customer messages"], read.Summary.CustomerMessages),
            ],
            [
                new ReportTable(
                    w["Channels"],
                    w["Activity by channel"],
                    [
                        Column(w["Channel"], ReportValueKind.Text),
                        Column(w["Conversations started"], ReportValueKind.Count),
                        Column(w["Customer messages"], ReportValueKind.Count),
                        Column(w["AI messages"], ReportValueKind.Count),
                        Column(w["Human messages"], ReportValueKind.Count),
                    ],
                    rows),
            ],
            [
                w["A message is attributed to the channel of the conversation it belongs to."],
                w[NoText],
            ]);
    }

    private static ReportDocument AiHumanHandling(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        AnalyticsAiOperations turns = read.AiOperations;
        AnalyticsSnapshot now = read.Current;

        return Document(
            ReportType.AiHumanHandling,
            period,
            workspace,
            at,
            w,
            [
                Fact(w["AI replies"], read.Summary.AiMessages),
                Fact(w["Human replies"], read.Summary.HumanMessages),
                Fact(w["AI turns attempted"], turns.Attempts),
                Fact(w["AI turns completed"], turns.Completed),
                Fact(w["AI turns failed"], turns.Failed),
                Fact(w["AI turns still in progress"], turns.InProgress),
            ],
            [
                Table(
                    w["Daily replies"],
                    w["Replies by day"],
                    [
                        Column(w.DateHeader(period.TimeZone), ReportValueKind.Date),
                        Column(w["AI replies"], ReportValueKind.Count),
                        Column(w["Human replies"], ReportValueKind.Count),
                    ],
                    read.DailyActivity
                        .OrderBy(day => day.Date)
                        .Select(day => Row(
                            ReportValue.Date(day.Date),
                            ReportValue.Count(day.AiMessages),
                            ReportValue.Count(day.HumanMessages)))),
                new ReportTable(
                    w["Current handling"],
                    w["Open conversations by handling (current snapshot)"],
                    [
                        Column(w["Handled by"], ReportValueKind.Text),
                        Column(w["Open conversations"], ReportValueKind.Count),
                    ],
                    [
                        Row(ReportValue.Text(w["AI"]), ReportValue.Count(now.OpenHandledByAi)),
                        Row(ReportValue.Text(w["A person"]), ReportValue.Count(now.OpenHandledByHuman)),
                    ]),
            ],
            [
                w["Open-conversation figures are a snapshot at the moment the report was generated, not the period."],
                w["An AI turn is one attempt to answer a customer; it is counted when it started."],
                w[NoText],
            ]);
    }

    private static ReportDocument CustomerActivity(
        IReadOnlyList<CustomerActivityRow> rows, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w) =>
        Document(
            ReportType.CustomerActivity,
            period,
            workspace,
            at,
            w,
            [Fact(w["Active customers"], rows.Count)],
            [
                Table(
                    w["Customers"],
                    w["Active customers"],
                    [
                        Column(w["Customer ID"], ReportValueKind.Text),
                        Column(w["Customer"], ReportValueKind.Text),
                        Column(w["Channels"], ReportValueKind.Text),
                        Column(w["Conversations active"], ReportValueKind.Count),
                        Column(w["Conversations started"], ReportValueKind.Count),
                        Column(w["Customer messages"], ReportValueKind.Count),
                        Column(w["First activity (UTC)"], ReportValueKind.Instant),
                        Column(w["Last activity (UTC)"], ReportValueKind.Instant),
                    ],
                    rows
                        .OrderByDescending(row => row.LastActivityAt)
                        .ThenByDescending(row => row.CustomerId.Value)
                        .Select(row => Row(
                            ReportValue.Text(row.CustomerId.ToString()),
                            ReportValue.Text(w.Customer(row.DisplayName, row.Channels)),
                            ReportValue.Text(w.Channels(row.Channels.OrderBy(kind => kind))),
                            ReportValue.Count(row.ConversationsActive),
                            ReportValue.Count(row.ConversationsStarted),
                            ReportValue.Count(row.CustomerMessages),
                            ReportValue.Instant(row.FirstActivityAt),
                            ReportValue.Instant(row.LastActivityAt)))),
            ],
            [
                w["A customer is active if a message was sent or a conversation was started in one of their conversations during the period."],
                w["Customers are canonical: records combined by a merge appear once."],
                w["No phone number, WhatsApp identifier, email address or chat credential is included."],
                w[NoText],
            ]);

    private static ReportDocument AgentActivity(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w) =>
        Document(
            ReportType.AgentActivity,
            period,
            workspace,
            at,
            w,
            [Fact(w["AI agents with activity"], read.Agents.Count)],
            [
                Table(
                    w["AI agents"],
                    w["Activity by AI agent"],
                    [
                        Column(w["AI agent ID"], ReportValueKind.Text),
                        Column(w["AI agent"], ReportValueKind.Text),
                        Column(w["Conversations started"], ReportValueKind.Count),
                        Column(w["Customer messages"], ReportValueKind.Count),
                        Column(w["AI replies"], ReportValueKind.Count),
                        Column(w["Human replies in its conversations"], ReportValueKind.Count),
                    ],
                    read.Agents.Select(agent => Row(
                        ReportValue.Text(agent.AiAgentId.ToString(w["D"], CultureInfo.InvariantCulture)),
                        ReportValue.Text(agent.Name),
                        ReportValue.Count(agent.ConversationsStarted),
                        ReportValue.Count(agent.CustomerMessages),
                        ReportValue.Count(agent.AiMessages),
                        ReportValue.Count(agent.HumanMessages)))),
            ],
            [
                w["Ordered by conversations started, then customer messages, then name, then id."],
                w["Human replies are counted in the conversations an agent was handling; they are not work the agent did."],
                w[NoText],
            ]);

    private static ReportDocument ProviderUsage(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        AnalyticsAiUsage usage = read.AiUsage;

        return Document(
            ReportType.ProviderUsage,
            period,
            workspace,
            at,
            w,
            [
                Fact(w["Provider calls"], usage.Calls),
                Fact(w["Text generation calls"], usage.GenerationCalls),
                Fact(w["Embedding calls"], usage.EmbeddingCalls),
                Fact(w["Generation input tokens"], usage.GenerationInputTokens),
                Fact(w["Generation cached input tokens"], usage.GenerationCachedInputTokens),
                Fact(w["Generation output tokens"], usage.GenerationOutputTokens),
                Fact(w["Generation reasoning tokens"], usage.GenerationReasoningTokens),
                Fact(w["Embedding input tokens"], usage.EmbeddingInputTokens),
            ],
            [
                Table(
                    w["Models"],
                    w["Calls by provider, model and operation"],
                    [
                        Column(w["Provider"], ReportValueKind.Text),
                        Column(w["Model"], ReportValueKind.Text),
                        Column(w["Operation"], ReportValueKind.Text),
                        Column(w["Calls"], ReportValueKind.Count),
                        Column(w["Input tokens"], ReportValueKind.Count),
                        Column(w["Output tokens"], ReportValueKind.Count),
                    ],
                    read.Models.Select(model => Row(
                        ReportValue.Text(w.Provider(model.Provider)),
                        ReportValue.Text(model.Model),
                        ReportValue.Text(w.Operation(model.OperationType)),
                        ReportValue.Count(model.Calls),
                        ReportValue.Count(model.InputTokens),
                        ReportValue.Count(model.OutputTokens)))),
                new ReportTable(
                    w["Outcomes"],
                    w["Call outcomes"],
                    [
                        Column(w["Outcome"], ReportValueKind.Text),
                        Column(w["Calls"], ReportValueKind.Count),
                    ],
                    [
                        Row(ReportValue.Text(w["Succeeded"]), ReportValue.Count(usage.Succeeded)),
                        Row(ReportValue.Text(w["Failed"]), ReportValue.Count(usage.Failed)),
                        Row(ReportValue.Text(w["Unknown (dispatched, outcome not established)"]), ReportValue.Count(usage.Unresolved)),
                    ]),
            ],
            [
                w["Calls are real provider requests, counted when they were dispatched."],
                w["Tokens are the providers' own reported figures; a call whose usage was not reported adds nothing."],
                w[NoText],
            ]);
    }

    private static ReportDocument ProviderCost(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        AnalyticsAiUsage usage = read.AiUsage;

        return Document(
            ReportType.ProviderCost,
            period,
            workspace,
            at,
            w,
            [
                Fact(w["Currencies"], usage.RatedCost.Count),
                Fact(w["Unrated calls"], usage.UnratedCalls),
            ],
            [
                Table(
                    w["Models"],
                    w["Estimated provider cost by model"],
                    [
                        Column(w["Provider"], ReportValueKind.Text),
                        Column(w["Model"], ReportValueKind.Text),
                        Column(w["Operation"], ReportValueKind.Text),
                        Column(w["Calls"], ReportValueKind.Count),
                        Column(w["Estimated provider cost"], ReportValueKind.Money),
                        Column(w["Currency"], ReportValueKind.Text),
                        Column(w["Unrated calls"], ReportValueKind.Count),
                    ],
                    read.Models.Select(model => Row(
                        ReportValue.Text(w.Provider(model.Provider)),
                        ReportValue.Text(model.Model),
                        ReportValue.Text(w.Operation(model.OperationType)),
                        ReportValue.Count(model.Calls),
                        ReportValue.Money(model.EstimatedCost),
                        ReportValue.Text(model.Currency),
                        ReportValue.Count(model.UnratedCalls)))),
                Table(
                    w["By currency"],
                    w["Estimated provider cost by currency"],
                    [
                        Column(w["Currency"], ReportValueKind.Text),
                        Column(w["Estimated provider cost"], ReportValueKind.Money),
                        Column(w["Rated calls"], ReportValueKind.Count),
                    ],
                    usage.RatedCost
                        .OrderBy(cost => cost.Currency, StringComparer.Ordinal)
                        .Select(cost => Row(
                            ReportValue.Text(cost.Currency),
                            ReportValue.Money(cost.EstimatedCost),
                            ReportValue.Count(cost.Calls)))),
            ],
            [
                w["Estimated provider cost is internal operational accounting of what AI providers were used for. "
                    + "It is not billing, and nothing in it is charged to anyone."],
                w["Currencies are never added together and nothing is converted: each currency has its own total."],
                w["A model with no cost shown was either not rated, or rated in more than one currency."],
                w["Unrated calls succeeded but had no configured rate. They are real usage, not free."],
            ]);
    }

    private static ReportDocument WhatsAppDelivery(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at, ReportWording w)
    {
        AnalyticsWhatsAppDelivery delivery = read.WhatsAppDelivery
            ?? new AnalyticsWhatsAppDelivery(0, 0, 0, 0, 0, 0, 0);

        (string Status, long Count)[] statuses =
        [
            (w["Pending"], delivery.Pending),
            (w["Sending"], delivery.Sending),
            (w["Sent"], delivery.Sent),
            (w["Delivered"], delivery.Delivered),
            (w["Read"], delivery.Read),
            (w["Failed"], delivery.Failed),
            (w["Unknown"], delivery.Unknown),
        ];

        return Document(
            ReportType.WhatsAppDelivery,
            period,
            workspace,
            at,
            w,
            [Fact(w["Outbound WhatsApp messages"], statuses.Sum(status => status.Count))],
            [
                Table(
                    w["Delivery status"],
                    w["Outbound messages by delivery status"],
                    [
                        Column(w["Status"], ReportValueKind.Text),
                        Column(w["Messages"], ReportValueKind.Count),
                    ],
                    statuses.Select(status => Row(
                        ReportValue.Text(status.Status),
                        ReportValue.Count(status.Count)))),
            ],
            [
                w["Messages are counted by when they were queued, at the status they have now."],
                w["Unknown means a send was interrupted and it cannot be established whether it arrived."],
                w[NoText],
            ]);
    }

    // ----------------------------------------------------------------- helpers

    private static ReportDocument Document(
        ReportType type,
        ReportPeriod period,
        string? workspace,
        DateTimeOffset generatedAt,
        ReportWording w,
        IReadOnlyList<ReportFact> summary,
        IReadOnlyList<ReportTable> tables,
        IReadOnlyList<string> notes) =>
        new(
            type,
            w.Title(type),
            workspace,
            period,
            generatedAt.ToUniversalTime(),
            summary,
            tables,
            [w.PeriodNote(period), .. notes],
            w.Language);

    private static ReportFact Fact(string label, long value) => new(label, ReportValue.Count(value));

    private static ReportColumn Column(string header, ReportValueKind kind) => new(header, kind);

    private static ReportValue[] Row(params ReportValue[] values) => values;

    private static ReportTable Table(
        string name,
        string title,
        IReadOnlyList<ReportColumn> columns,
        IEnumerable<IReadOnlyList<ReportValue>> rows) =>
        new(name, title, columns, [.. rows]);
}
