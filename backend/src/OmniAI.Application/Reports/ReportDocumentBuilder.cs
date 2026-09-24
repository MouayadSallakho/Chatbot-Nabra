using System.Globalization;
using OmniAI.Application.Analytics;
using OmniAI.Application.Customers;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;

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

    /// <exception cref="ReportTooLargeException">The report would carry more than <paramref name="rowLimit"/> rows.</exception>
    public async Task<ReportDocument> BuildAsync(
        TenantId tenantId,
        ReportType type,
        ReportPeriod period,
        string? workspaceName,
        DateTimeOffset generatedAt,
        int rowLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowLimit);

        AnalyticsWindow window = AnalyticsWindow.ForDays(period.FromDate, period.ToExclusiveDate);

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

            document = CustomerActivity(rows, period, workspaceName, generatedAt);
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
                ReportType.ConversationActivity => ConversationActivity(read, period, workspaceName, generatedAt),
                ReportType.ChannelUsage => ChannelUsage(read, period, workspaceName, generatedAt),
                ReportType.AiHumanHandling => AiHumanHandling(read, period, workspaceName, generatedAt),
                ReportType.AgentActivity => AgentActivity(read, period, workspaceName, generatedAt),
                ReportType.ProviderUsage => ProviderUsage(read, period, workspaceName, generatedAt),
                ReportType.ProviderCost => ProviderCost(read, period, workspaceName, generatedAt),
                ReportType.WhatsAppDelivery => WhatsAppDelivery(read, period, workspaceName, generatedAt),
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
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
    {
        AnalyticsSummary summary = read.Summary;
        AnalyticsSnapshot now = read.Current;

        return Document(
            ReportType.ConversationActivity,
            period,
            workspace,
            at,
            [
                Fact("Conversations started", summary.ConversationsStarted),
                Fact("Customer messages", summary.CustomerMessages),
                Fact("AI messages", summary.AiMessages),
                Fact("Human messages", summary.HumanMessages),
                Fact("System messages", summary.SystemMessages),
                Fact("Open conversations (current snapshot)", now.OpenConversations),
                Fact("Open, handled by AI (current snapshot)", now.OpenHandledByAi),
                Fact("Open, handled by a person (current snapshot)", now.OpenHandledByHuman),
            ],
            [
                Table(
                    "Daily activity",
                    "Daily activity",
                    [
                        Column("Date (UTC)", ReportValueKind.Date),
                        Column("Conversations started", ReportValueKind.Count),
                        Column("Customer messages", ReportValueKind.Count),
                        Column("AI messages", ReportValueKind.Count),
                        Column("Human messages", ReportValueKind.Count),
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
                "Snapshot figures describe the workspace at the moment the report was generated, not the period.",
                NoText,
            ]);
    }

    private static ReportDocument ChannelUsage(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
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
                    ReportValue.Text(ChannelLabel(kind)),
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
            [
                Fact("Conversations started", read.Summary.ConversationsStarted),
                Fact("Customer messages", read.Summary.CustomerMessages),
            ],
            [
                new ReportTable(
                    "Channels",
                    "Activity by channel",
                    [
                        Column("Channel", ReportValueKind.Text),
                        Column("Conversations started", ReportValueKind.Count),
                        Column("Customer messages", ReportValueKind.Count),
                        Column("AI messages", ReportValueKind.Count),
                        Column("Human messages", ReportValueKind.Count),
                    ],
                    rows),
            ],
            [
                "A message is attributed to the channel of the conversation it belongs to.",
                NoText,
            ]);
    }

    private static ReportDocument AiHumanHandling(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
    {
        AnalyticsAiOperations turns = read.AiOperations;
        AnalyticsSnapshot now = read.Current;

        return Document(
            ReportType.AiHumanHandling,
            period,
            workspace,
            at,
            [
                Fact("AI replies", read.Summary.AiMessages),
                Fact("Human replies", read.Summary.HumanMessages),
                Fact("AI turns attempted", turns.Attempts),
                Fact("AI turns completed", turns.Completed),
                Fact("AI turns failed", turns.Failed),
                Fact("AI turns still in progress", turns.InProgress),
            ],
            [
                Table(
                    "Daily replies",
                    "Replies by day",
                    [
                        Column("Date (UTC)", ReportValueKind.Date),
                        Column("AI replies", ReportValueKind.Count),
                        Column("Human replies", ReportValueKind.Count),
                    ],
                    read.DailyActivity
                        .OrderBy(day => day.Date)
                        .Select(day => Row(
                            ReportValue.Date(day.Date),
                            ReportValue.Count(day.AiMessages),
                            ReportValue.Count(day.HumanMessages)))),
                new ReportTable(
                    "Current handling",
                    "Open conversations by handling (current snapshot)",
                    [
                        Column("Handled by", ReportValueKind.Text),
                        Column("Open conversations", ReportValueKind.Count),
                    ],
                    [
                        Row(ReportValue.Text("AI"), ReportValue.Count(now.OpenHandledByAi)),
                        Row(ReportValue.Text("A person"), ReportValue.Count(now.OpenHandledByHuman)),
                    ]),
            ],
            [
                "Open-conversation figures are a snapshot at the moment the report was generated, not the period.",
                "An AI turn is one attempt to answer a customer; it is counted when it started.",
                NoText,
            ]);
    }

    private static ReportDocument CustomerActivity(
        IReadOnlyList<CustomerActivityRow> rows, ReportPeriod period, string? workspace, DateTimeOffset at) =>
        Document(
            ReportType.CustomerActivity,
            period,
            workspace,
            at,
            [Fact("Active customers", rows.Count)],
            [
                Table(
                    "Customers",
                    "Active customers",
                    [
                        Column("Customer ID", ReportValueKind.Text),
                        Column("Customer", ReportValueKind.Text),
                        Column("Channels", ReportValueKind.Text),
                        Column("Conversations active", ReportValueKind.Count),
                        Column("Conversations started", ReportValueKind.Count),
                        Column("Customer messages", ReportValueKind.Count),
                        Column("First activity (UTC)", ReportValueKind.Instant),
                        Column("Last activity (UTC)", ReportValueKind.Instant),
                    ],
                    rows
                        .OrderByDescending(row => row.LastActivityAt)
                        .ThenByDescending(row => row.CustomerId.Value)
                        .Select(row => Row(
                            ReportValue.Text(row.CustomerId.ToString()),
                            ReportValue.Text(CustomerLabels.For(row.DisplayName, row.Channels)),
                            ReportValue.Text(string.Join(", ", row.Channels.OrderBy(kind => kind).Select(ChannelLabel))),
                            ReportValue.Count(row.ConversationsActive),
                            ReportValue.Count(row.ConversationsStarted),
                            ReportValue.Count(row.CustomerMessages),
                            ReportValue.Instant(row.FirstActivityAt),
                            ReportValue.Instant(row.LastActivityAt)))),
            ],
            [
                "A customer is active if a message was sent or a conversation was started in one of their conversations during the period.",
                "Customers are canonical: records combined by a merge appear once.",
                "No phone number, WhatsApp identifier, email address or chat credential is included.",
                NoText,
            ]);

    private static ReportDocument AgentActivity(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at) =>
        Document(
            ReportType.AgentActivity,
            period,
            workspace,
            at,
            [Fact("AI agents with activity", read.Agents.Count)],
            [
                Table(
                    "AI agents",
                    "Activity by AI agent",
                    [
                        Column("AI agent ID", ReportValueKind.Text),
                        Column("AI agent", ReportValueKind.Text),
                        Column("Conversations started", ReportValueKind.Count),
                        Column("Customer messages", ReportValueKind.Count),
                        Column("AI replies", ReportValueKind.Count),
                        Column("Human replies in its conversations", ReportValueKind.Count),
                    ],
                    read.Agents.Select(agent => Row(
                        ReportValue.Text(agent.AiAgentId.ToString("D", CultureInfo.InvariantCulture)),
                        ReportValue.Text(agent.Name),
                        ReportValue.Count(agent.ConversationsStarted),
                        ReportValue.Count(agent.CustomerMessages),
                        ReportValue.Count(agent.AiMessages),
                        ReportValue.Count(agent.HumanMessages)))),
            ],
            [
                "Ordered by conversations started, then customer messages, then name, then id.",
                "Human replies are counted in the conversations an agent was handling; they are not work the agent did.",
                NoText,
            ]);

    private static ReportDocument ProviderUsage(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
    {
        AnalyticsAiUsage usage = read.AiUsage;

        return Document(
            ReportType.ProviderUsage,
            period,
            workspace,
            at,
            [
                Fact("Provider calls", usage.Calls),
                Fact("Text generation calls", usage.GenerationCalls),
                Fact("Embedding calls", usage.EmbeddingCalls),
                Fact("Generation input tokens", usage.GenerationInputTokens),
                Fact("Generation cached input tokens", usage.GenerationCachedInputTokens),
                Fact("Generation output tokens", usage.GenerationOutputTokens),
                Fact("Generation reasoning tokens", usage.GenerationReasoningTokens),
                Fact("Embedding input tokens", usage.EmbeddingInputTokens),
            ],
            [
                Table(
                    "Models",
                    "Calls by provider, model and operation",
                    [
                        Column("Provider", ReportValueKind.Text),
                        Column("Model", ReportValueKind.Text),
                        Column("Operation", ReportValueKind.Text),
                        Column("Calls", ReportValueKind.Count),
                        Column("Input tokens", ReportValueKind.Count),
                        Column("Output tokens", ReportValueKind.Count),
                    ],
                    read.Models.Select(model => Row(
                        ReportValue.Text(model.Provider),
                        ReportValue.Text(model.Model),
                        ReportValue.Text(OperationLabel(model.OperationType)),
                        ReportValue.Count(model.Calls),
                        ReportValue.Count(model.InputTokens),
                        ReportValue.Count(model.OutputTokens)))),
                new ReportTable(
                    "Outcomes",
                    "Call outcomes",
                    [
                        Column("Outcome", ReportValueKind.Text),
                        Column("Calls", ReportValueKind.Count),
                    ],
                    [
                        Row(ReportValue.Text("Succeeded"), ReportValue.Count(usage.Succeeded)),
                        Row(ReportValue.Text("Failed"), ReportValue.Count(usage.Failed)),
                        Row(ReportValue.Text("Unknown (dispatched, outcome not established)"), ReportValue.Count(usage.Unresolved)),
                    ]),
            ],
            [
                "Calls are real provider requests, counted when they were dispatched.",
                "Tokens are the providers' own reported figures; a call whose usage was not reported adds nothing.",
                NoText,
            ]);
    }

    private static ReportDocument ProviderCost(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
    {
        AnalyticsAiUsage usage = read.AiUsage;

        return Document(
            ReportType.ProviderCost,
            period,
            workspace,
            at,
            [
                Fact("Currencies", usage.RatedCost.Count),
                Fact("Unrated calls", usage.UnratedCalls),
            ],
            [
                Table(
                    "Models",
                    "Estimated provider cost by model",
                    [
                        Column("Provider", ReportValueKind.Text),
                        Column("Model", ReportValueKind.Text),
                        Column("Operation", ReportValueKind.Text),
                        Column("Calls", ReportValueKind.Count),
                        Column("Estimated provider cost", ReportValueKind.Money),
                        Column("Currency", ReportValueKind.Text),
                        Column("Unrated calls", ReportValueKind.Count),
                    ],
                    read.Models.Select(model => Row(
                        ReportValue.Text(model.Provider),
                        ReportValue.Text(model.Model),
                        ReportValue.Text(OperationLabel(model.OperationType)),
                        ReportValue.Count(model.Calls),
                        ReportValue.Money(model.EstimatedCost),
                        ReportValue.Text(model.Currency),
                        ReportValue.Count(model.UnratedCalls)))),
                Table(
                    "By currency",
                    "Estimated provider cost by currency",
                    [
                        Column("Currency", ReportValueKind.Text),
                        Column("Estimated provider cost", ReportValueKind.Money),
                        Column("Rated calls", ReportValueKind.Count),
                    ],
                    usage.RatedCost
                        .OrderBy(cost => cost.Currency, StringComparer.Ordinal)
                        .Select(cost => Row(
                            ReportValue.Text(cost.Currency),
                            ReportValue.Money(cost.EstimatedCost),
                            ReportValue.Count(cost.Calls)))),
            ],
            [
                "Estimated provider cost is internal operational accounting of what AI providers were used for. "
                + "It is not billing, and nothing in it is charged to anyone.",
                "Currencies are never added together and nothing is converted: each currency has its own total.",
                "A model with no cost shown was either not rated, or rated in more than one currency.",
                "Unrated calls succeeded but had no configured rate. They are real usage, not free.",
            ]);
    }

    private static ReportDocument WhatsAppDelivery(
        WorkspaceAnalytics read, ReportPeriod period, string? workspace, DateTimeOffset at)
    {
        AnalyticsWhatsAppDelivery delivery = read.WhatsAppDelivery
            ?? new AnalyticsWhatsAppDelivery(0, 0, 0, 0, 0, 0, 0);

        (string Status, long Count)[] statuses =
        [
            ("Pending", delivery.Pending),
            ("Sending", delivery.Sending),
            ("Sent", delivery.Sent),
            ("Delivered", delivery.Delivered),
            ("Read", delivery.Read),
            ("Failed", delivery.Failed),
            ("Unknown", delivery.Unknown),
        ];

        return Document(
            ReportType.WhatsAppDelivery,
            period,
            workspace,
            at,
            [Fact("Outbound WhatsApp messages", statuses.Sum(status => status.Count))],
            [
                Table(
                    "Delivery status",
                    "Outbound messages by delivery status",
                    [
                        Column("Status", ReportValueKind.Text),
                        Column("Messages", ReportValueKind.Count),
                    ],
                    statuses.Select(status => Row(
                        ReportValue.Text(status.Status),
                        ReportValue.Count(status.Count)))),
            ],
            [
                "Messages are counted by when they were queued, at the status they have now.",
                "Unknown means a send was interrupted and it cannot be established whether it arrived.",
                NoText,
            ]);
    }

    // ----------------------------------------------------------------- helpers

    private static ReportDocument Document(
        ReportType type,
        ReportPeriod period,
        string? workspace,
        DateTimeOffset generatedAt,
        IReadOnlyList<ReportFact> summary,
        IReadOnlyList<ReportTable> tables,
        IReadOnlyList<string> notes) =>
        new(
            type,
            ReportCatalog.For(type).Title,
            workspace,
            period,
            generatedAt.ToUniversalTime(),
            summary,
            tables,
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Period: {period.FromDate:yyyy-MM-dd} to {period.LastDate:yyyy-MM-dd} inclusive, in UTC whole days."),
                .. notes,
            ]);

    private static ReportFact Fact(string label, long value) => new(label, ReportValue.Count(value));

    private static ReportColumn Column(string header, ReportValueKind kind) => new(header, kind);

    private static ReportValue[] Row(params ReportValue[] values) => values;

    private static ReportTable Table(
        string name,
        string title,
        IReadOnlyList<ReportColumn> columns,
        IEnumerable<IReadOnlyList<ReportValue>> rows) =>
        new(name, title, columns, [.. rows]);

    private static string ChannelLabel(ChannelKind channel) => channel switch
    {
        ChannelKind.WebChat => "Website",
        _ => channel.ToString(),
    };

    private static string OperationLabel(AiOperationType operation) => operation switch
    {
        AiOperationType.TextGeneration => "Text generation",
        AiOperationType.Embedding => "Embedding",
        _ => operation.ToString(),
    };
}
