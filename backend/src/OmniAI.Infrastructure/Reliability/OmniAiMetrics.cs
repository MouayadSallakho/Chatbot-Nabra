using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OmniAI.Infrastructure.Reliability;

/// <summary>The low-cardinality values the <c>kind</c> tag of a reconciliation may take.</summary>
internal static class ReconciledKinds
{
    public const string ProviderUsage = "provider_usage";
    public const string KnowledgeAttempt = "knowledge_attempt";
    public const string ReportOrphan = "report_orphan";
    public const string ReportMissingFile = "report_missing_file";
}

/// <summary>
/// The platform's operational metrics, on the built-in <see cref="Meter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Application-side only.</strong> Nothing here exports anywhere; any
/// listener — <c>dotnet-counters</c> today, an OpenTelemetry exporter chosen at
/// deployment later — can subscribe to the <c>OmniAI</c> meter without the code
/// changing.
/// </para>
/// <para>
/// <strong>Low cardinality by construction.</strong> Every tag value is a fixed
/// word from code — a worker name, an outcome, a status. No workspace, user,
/// customer, conversation, email address, phone number or provider id is ever a
/// dimension, so metrics can neither explode nor leak.
/// </para>
/// </remarks>
internal static class OmniAiMetrics
{
    public const string MeterName = "OmniAI";

    private static readonly Meter Meter = new(MeterName, "1.0");

    private static readonly Counter<long> WorkItems = Meter.CreateCounter<long>(
        "omniai.worker.items",
        description: "Durable work items a background worker finished, by worker and outcome.");

    private static readonly Counter<long> ReconciledItems = Meter.CreateCounter<long>(
        "omniai.reconciliation.settled",
        description: "Stale or inconsistent work settled by a reconciler, by kind.");

    private static readonly Histogram<double> ReportDuration = Meter.CreateHistogram<double>(
        "omniai.report.generation.duration",
        unit: "ms",
        description: "Time to generate one report, by outcome.");

    private static readonly Counter<long> ProviderOutcomes = Meter.CreateCounter<long>(
        "omniai.provider.operations",
        description: "Accounted AI provider operations settled, by status.");

    private static readonly Counter<long> EmailOutcomes = Meter.CreateCounter<long>(
        "omniai.notification.emails",
        description: "Operational emails settled, by status.");

    private static readonly Counter<long> KnowledgeOutcomes = Meter.CreateCounter<long>(
        "omniai.knowledge.index.attempts",
        description: "Knowledge indexing attempts settled, by status.");

    public static void WorkItem(string worker, string outcome) =>
        WorkItems.Add(1, new TagList { { "worker", worker }, { "outcome", outcome } });

    public static void Reconciled(string kind, int count)
    {
        if (count > 0)
        {
            ReconciledItems.Add(count, new TagList { { "kind", kind } });
        }
    }

    public static void ReportGenerated(string outcome, TimeSpan elapsed) =>
        ReportDuration.Record(elapsed.TotalMilliseconds, new TagList { { "outcome", outcome } });

    public static void ProviderOperation(string status) =>
        ProviderOutcomes.Add(1, new TagList { { "status", status } });

    public static void Email(string status) =>
        EmailOutcomes.Add(1, new TagList { { "status", status } });

    public static void KnowledgeAttempt(string status) =>
        KnowledgeOutcomes.Add(1, new TagList { { "status", status } });
}

/// <summary>
/// Correlation for background work.
/// </summary>
/// <remarks>
/// <para>
/// A request gets an <see cref="Activity"/> from ASP.NET Core, so every log line
/// it writes carries one TraceId, and its ProblemDetails carries the same one.
/// A background work item gets nothing by default, so its log lines could not be
/// told apart from its neighbours'. Each durable work item starts its own
/// activity here, and the logging pipeline's activity tracking puts its TraceId
/// on every line the item writes — including the provider calls it makes.
/// </para>
/// <para>
/// Created directly rather than from an <see cref="ActivitySource"/>, because a
/// source yields nothing without a listener and there is deliberately no
/// exporter. The name is a fixed word; no id, tenant or payload is attached.
/// </para>
/// </remarks>
internal static class OmniAiTracing
{
    public static Activity StartWork(string worker) =>
        new Activity($"omniai.worker.{worker}").Start();
}
