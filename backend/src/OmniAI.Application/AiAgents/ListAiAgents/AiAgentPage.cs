namespace OmniAI.Application.AiAgents.ListAiAgents;

/// <summary>
/// One page of a tenant's AI agents.
/// </summary>
/// <param name="Items">The agents on this page, in the query's order.</param>
/// <param name="Page">The 1-based page number that was served.</param>
/// <param name="PageSize">The maximum number of items a page may hold.</param>
/// <param name="TotalCount">How many agents the tenant has in total.</param>
/// <remarks>
/// <para>
/// Explicitly paginated from the first day this list exists. An endpoint that
/// returns "all of them" is a contract that cannot be withdrawn later without
/// breaking every client written against it, and it becomes a table scan the
/// moment one tenant is unusually large. Agent counts are expected to be modest
/// for a long time, which is exactly why the bound is cheap to add now and
/// expensive to add once callers depend on its absence.
/// </para>
/// <para>
/// <c>TotalCount</c> is here so a reader can be told what they are looking at --
/// "1-20 of 34" rather than a next button that may or may not do anything. It
/// costs a second query; at these sizes that is not a trade worth optimising
/// away, and if it ever becomes one the answer is a cheaper count, not a less
/// honest interface.
/// </para>
/// <para>
/// Deliberately not a generic <c>Page&lt;T&gt;</c>. One list exists. A shared
/// pagination type would be an abstraction invented for a second caller that has
/// not been designed yet, and every future list can have this shape without
/// inheriting a framework.
/// </para>
/// </remarks>
public sealed record AiAgentPage(
    IReadOnlyList<AiAgentSummary> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>The page served when a caller does not ask for one.</summary>
    public const int DefaultPage = 1;

    /// <summary>The page size served when a caller does not ask for one.</summary>
    /// <remarks>
    /// Twenty rows: comfortably more than a first-time tenant will have, and
    /// still a screenful rather than a scroll marathon.
    /// </remarks>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page a caller may request.</summary>
    /// <remarks>
    /// The bound that stops this endpoint becoming an unbounded read. A caller
    /// asking for more is refused rather than quietly given a hundred, because
    /// silently returning fewer rows than requested is how a client concludes it
    /// has reached the end of a list when it has not.
    /// </remarks>
    public const int MaxPageSize = 100;

    /// <summary>An empty page, for a tenant with no agents at all.</summary>
    public static AiAgentPage Empty(int page, int pageSize) =>
        new([], page, pageSize, 0);
}
