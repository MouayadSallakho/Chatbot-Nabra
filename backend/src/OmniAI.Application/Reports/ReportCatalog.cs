using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;

namespace OmniAI.Application.Reports;

/// <summary>
/// What one report is, who may produce it, and how it may be written.
/// </summary>
/// <param name="DataPermissions">
/// What the report's <em>data</em> needs, beyond the reports permissions
/// themselves. Generating needs <c>reports.generate</c> plus these; downloading
/// needs <c>reports.view</c> plus these; scheduling needs
/// <c>reports.schedule</c>, <c>reports.generate</c> and these. A report is never a
/// way to read something the caller could not read on its own screen.
/// </param>
public sealed record ReportDefinition(
    ReportType Type,
    string Slug,
    string Title,
    string Description,
    IReadOnlyList<ReportFormat> Formats,
    IReadOnlyList<PermissionCode> DataPermissions)
{
    public bool Supports(ReportFormat format) => Formats.Contains(format);
}

/// <summary>
/// The closed catalogue of reports, and the permission matrix that governs it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written in code, not configured.</strong> A report's permission
/// requirement is a security decision a reviewer must be able to see in a diff,
/// not a row somebody edits.
/// </para>
/// <para>
/// <strong>Aggregates and safe labels only.</strong> Every report here is counts
/// grouped by day, channel, agent, model, status or canonical customer. None
/// carries message text, knowledge text, agent instructions, a phone number or a
/// provider identifier — which is why none of them needs
/// <c>conversations.view</c>. A future report that exported conversation detail
/// would have to name it here.
/// </para>
/// </remarks>
public static class ReportCatalog
{
    private static readonly IReadOnlyList<ReportFormat> AllFormats =
        [ReportFormat.Csv, ReportFormat.Xlsx, ReportFormat.Pdf];

    private static readonly IReadOnlyList<PermissionCode> Analytics = [PlatformPermissions.AnalyticsView];

    /// <summary>The catalogue, in the order it is published.</summary>
    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new(
            ReportType.ConversationActivity,
            "conversation-activity",
            "Conversation activity",
            "Conversations started and messages sent by customers, AI agents and people, day by day.",
            AllFormats,
            Analytics),
        new(
            ReportType.ChannelUsage,
            "channel-usage",
            "Channel usage",
            "Conversations and messages on each channel the workspace used.",
            AllFormats,
            Analytics),
        new(
            ReportType.AiHumanHandling,
            "ai-human-handling",
            "AI and human handling",
            "Replies written by AI agents and by people, AI turn outcomes, and who is handling open conversations now.",
            AllFormats,
            Analytics),
        new(
            ReportType.CustomerActivity,
            "customer-activity",
            "Customer activity",
            "Each customer who was active in the period: channels, conversations, messages and first and last activity. No contact details.",
            AllFormats,
            [PlatformPermissions.CustomersView]),
        new(
            ReportType.AgentActivity,
            "agent-activity",
            "AI agent activity",
            "Conversations, customer messages, AI replies and human replies in each AI agent's conversations.",
            AllFormats,
            Analytics),
        new(
            ReportType.ProviderUsage,
            "provider-usage",
            "AI provider usage",
            "Calls to AI providers by model and operation, their outcomes, and the tokens the providers reported.",
            AllFormats,
            Analytics),
        new(
            ReportType.ProviderCost,
            "provider-cost",
            "Estimated provider cost",
            "Estimated provider cost by model and currency, and the calls no rate was configured for. Internal accounting, not an invoice.",
            AllFormats,
            Analytics),
        new(
            ReportType.WhatsAppDelivery,
            "whatsapp-delivery",
            "WhatsApp delivery status",
            "Where the workspace's outbound WhatsApp messages got to, by delivery status.",
            AllFormats,
            Analytics),
    ];

    /// <summary>The code of the period parameter of a report request (Phase 37.6, row 32).</summary>
    public const string RangeParameter = "range";

    /// <summary>The code of the file-format parameter of a report request (Phase 37.6, row 32).</summary>
    public const string FormatParameter = "format";

    /// <summary>The <c>range</c> value that means "two explicit dates".</summary>
    public const string CustomRange = "custom";

    public static ReportDefinition For(ReportType type) =>
        All.FirstOrDefault(definition => definition.Type == type)
        ?? throw new ArgumentOutOfRangeException(nameof(type), type, "That report type is not in the catalogue.");

    /// <summary>Everything generating this report requires.</summary>
    public static IReadOnlyList<PermissionCode> ToGenerate(ReportType type) =>
        [PlatformPermissions.ReportsGenerate, .. For(type).DataPermissions];

    /// <summary>Everything downloading a generated copy of this report requires.</summary>
    public static IReadOnlyList<PermissionCode> ToDownload(ReportType type) =>
        [PlatformPermissions.ReportsView, .. For(type).DataPermissions];

    /// <summary>Everything owning a schedule for this report requires, at every occurrence.</summary>
    public static IReadOnlyList<PermissionCode> ToSchedule(ReportType type) =>
        [PlatformPermissions.ReportsSchedule, .. ToGenerate(type)];

    public static bool Holds(IReadOnlyCollection<PermissionCode> held, IEnumerable<PermissionCode> required)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(required);

        return required.All(held.Contains);
    }

    public static string ContentType(ReportFormat format) => format switch
    {
        ReportFormat.Csv => "text/csv; charset=utf-8",
        ReportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ReportFormat.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Csv => "csv",
        ReportFormat.Xlsx => "xlsx",
        ReportFormat.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>
    /// The name a downloaded file is given.
    /// </summary>
    /// <remarks>
    /// Built only from server-controlled parts: a fixed slug, two ISO dates and a
    /// fixed extension. No workspace name, customer name or schedule name ever
    /// reaches a <c>Content-Disposition</c> header, so there is nothing a user
    /// could type that ends up in one.
    /// </remarks>
    public static string FileName(ReportType type, ReportPeriod period, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(period);

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{For(type).Slug}-{period.FromDate:yyyy-MM-dd}-to-{period.LastDate:yyyy-MM-dd}.{Extension(format)}");
    }
}
