namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// The load scenarios run on their own, after everything else.
/// </summary>
/// <remarks>
/// They deliberately drive tens of concurrent turns, each holding pooled
/// connections, against the one PostgreSQL server the whole suite shares. Run
/// beside the other test classes (each with its own host and pools) they pushed
/// that server past its connection limit and turned other suites' requests into
/// 503s. Isolating them keeps both the scenarios and the rest of the suite honest.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoadScenarios
{
    public const string Name = "Load scenarios";
}
