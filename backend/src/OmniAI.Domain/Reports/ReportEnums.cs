namespace OmniAI.Domain.Reports;

/// <summary>
/// Every report this platform can produce.
/// </summary>
/// <remarks>
/// <strong>A closed catalogue, not a query language.</strong> Each value names a
/// report whose columns, ordering and permission requirement are written in code.
/// There is no report builder, no field picker and no stored query: a caller
/// chooses one of these and a period, and nothing else about what is read.
/// </remarks>
public enum ReportType
{
    ConversationActivity = 1,
    ChannelUsage = 2,
    AiHumanHandling = 3,
    CustomerActivity = 4,
    AgentActivity = 5,
    ProviderUsage = 6,
    ProviderCost = 7,
    WhatsAppDelivery = 8,
}

/// <summary>The file a report is written as.</summary>
public enum ReportFormat
{
    Csv = 1,
    Xlsx = 2,
    Pdf = 3,
}

/// <summary>
/// Where one generation got to.
/// </summary>
/// <remarks>
/// <see cref="Expired"/> is the only state beyond the four a queue needs, and it
/// exists so a list can say truthfully why a finished report can no longer be
/// downloaded: its file was removed by the retention policy, not lost.
/// </remarks>
public enum ReportExecutionStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5,
}

/// <summary>How often a schedule produces a report. Deliberately two values.</summary>
public enum ReportFrequency
{
    /// <summary>Every Monday, for the seven UTC days that just ended.</summary>
    Weekly = 1,

    /// <summary>The first of every month, for the UTC calendar month that just ended.</summary>
    Monthly = 2,
}

/// <summary>
/// Why a generation failed, in words a workspace administrator can act on.
/// </summary>
/// <remarks>
/// Stable codes with fixed sentences. An exception message never reaches a
/// report list: it can carry a file path, a SQL fragment or a provider's text, and
/// none of those belongs in front of a customer's administrator.
/// </remarks>
public static class ReportFailures
{
    public const string TooLarge = "report_too_large";
    public const string PermissionRevoked = "permission_revoked";
    public const string StorageUnavailable = "storage_unavailable";
    public const string GenerationFailed = "generation_failed";

    /// <summary>The sentence shown for a code. Unknown codes get the generic one.</summary>
    public static string Describe(string? code) => code switch
    {
        TooLarge => "Report exceeds the supported export size. Choose a shorter period.",
        PermissionRevoked =>
            "The member who requested this report no longer holds the permissions it needs, "
            + "so it was not generated.",
        StorageUnavailable =>
            "Report storage is not configured on this deployment, so the report could not be saved.",
        _ => "The report could not be generated. Try again, and contact an administrator if it continues.",
    };
}
