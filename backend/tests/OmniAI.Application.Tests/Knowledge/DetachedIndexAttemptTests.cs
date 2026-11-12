using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Phase 38.7a: an attempt the HTTP route runs does not depend on the browser
/// (BA-10), and its start and settlement are announced to knowledge viewers
/// (BA-11).
/// </summary>
/// <remarks>
/// The handler is wired exactly as the composition root wires it; the provider
/// double can hold an attempt open until a test lets it go, or until a token
/// stops it.
/// </remarks>
public sealed class DetachedIndexAttemptTests
{
    // ---- The attempt and its record (BA-10) -----------------------------------

    [Fact]
    public async Task A_detached_attempt_indexes_and_settles_as_before()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        Versioned<KnowledgeIndexResult>? result = await harness.IndexDetachedAsync();

        Assert.NotNull(result);
        Assert.False(result.Value.AlreadyCurrent);
        Assert.Equal("Ready", result.Value.Document.Readiness.ToString());
        Assert.Equal(1, harness.Index.Replacements);
        Assert.Equal(KnowledgeIndexAttemptStatus.Succeeded, Assert.Single(harness.Attempts.Settled).Status);
    }

    [Fact]
    public async Task Running_past_the_servers_limit_is_recorded_as_timed_out_not_as_the_persons_cancellation()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build(
            limits: new KnowledgeIndexingLimits(TimeSpan.FromMilliseconds(200)));

        // The provider never answers.
        harness.Embeddings.HoldUntil = new TaskCompletionSource().Task;

        // A caller still waiting hears what a provider timeout says (504).
        await Assert.ThrowsAsync<EmbeddingTimeoutException>(() => harness.IndexDetachedAsync());

        RecordingAttemptStore.Settlement settled = Assert.Single(harness.Attempts.Settled);

        Assert.Equal(KnowledgeIndexAttemptStatus.Failed, settled.Status);
        Assert.Equal(KnowledgeIndexAttemptFailures.TimedOut, settled.Code);
        Assert.True(settled.Alert);
        Assert.Equal(0, harness.Index.Replacements);
    }

    [Fact]
    public async Task A_host_shutting_down_is_recorded_as_interrupted_like_the_reconciler_does()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();
        using CancellationTokenSource stopping = new();

        // Stopped once the attempt is held by the provider.
        harness.Embeddings.HoldUntil = new TaskCompletionSource().Task;
        harness.Embeddings.BeforeCall = () => stopping.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.IndexDetachedAsync(stopping.Token));

        RecordingAttemptStore.Settlement settled = Assert.Single(harness.Attempts.Settled);

        Assert.Equal(KnowledgeIndexAttemptStatus.Unknown, settled.Status);
        Assert.Equal(KnowledgeIndexAttemptFailures.Interrupted, settled.Code);
        Assert.True(settled.Alert);
    }

    [Fact]
    public async Task A_caller_that_runs_the_handler_with_its_own_token_is_still_recorded_as_cancelled()
    {
        // The handler's own contract is unchanged for every caller but the route.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();
        using CancellationTokenSource person = new();

        harness.Embeddings.HoldUntil = new TaskCompletionSource().Task;
        harness.Embeddings.BeforeCall = () => person.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Handler.HandleAsync(
            new IndexKnowledgeDocumentCommand(
                harness.Workspace.Value,
                harness.Document.Id.Value,
                harness.Documents.VersionOf(harness.Document)),
            person.Token));

        RecordingAttemptStore.Settlement settled = Assert.Single(harness.Attempts.Settled);

        Assert.Equal(KnowledgeIndexAttemptStatus.Unknown, settled.Status);
        Assert.Equal(KnowledgeIndexAttemptFailures.Cancelled, settled.Code);
        Assert.False(settled.Alert);
    }

    // ---- The signal (BA-11) -----------------------------------------------------

    [Fact]
    public async Task An_attempt_is_announced_when_it_starts_and_again_when_it_settles()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        // What had been recorded at the moment of each announcement.
        List<(int Started, int Settled)> seen = [];
        harness.Announcer.Observe = () => seen.Add((harness.Attempts.Started.Count, harness.Attempts.Settled.Count));

        await harness.IndexDetachedAsync();

        Assert.Equal([(1, 0), (1, 1)], seen);
        Assert.All(harness.Announcer.Announced, announced =>
        {
            Assert.Equal(harness.Workspace, announced.Tenant);
            Assert.Equal(harness.Document.Id, announced.Document);
        });
    }

    [Fact]
    public async Task A_failed_attempt_is_announced_when_it_starts_and_again_when_it_settles()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();
        harness.Embeddings.PretendUnavailable = true;

        List<(int Started, int Settled)> seen = [];
        harness.Announcer.Observe = () => seen.Add((harness.Attempts.Started.Count, harness.Attempts.Settled.Count));

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => harness.IndexDetachedAsync());

        Assert.Equal([(1, 0), (1, 1)], seen);
        Assert.Equal(KnowledgeIndexAttemptFailures.ProviderUnavailable, Assert.Single(harness.Attempts.Settled).Code);
    }

    [Fact]
    public async Task Nothing_is_announced_when_there_is_no_attempt()
    {
        // An index that is already current costs nothing, records nothing and
        // so announces nothing; a refusal before any work announces nothing either.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        await harness.IndexDetachedAsync();
        int afterFirst = harness.Announcer.Announced.Count;

        Versioned<KnowledgeIndexResult>? again = await harness.IndexDetachedAsync();

        Assert.True(again!.Value.AlreadyCurrent);
        Assert.Equal(afterFirst, harness.Announcer.Announced.Count);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.Handler.HandleDetachedAsync(
            new IndexKnowledgeDocumentCommand(
                harness.Workspace.Value,
                harness.Document.Id.Value,
                ConcurrencyToken.From("stale")),
            CancellationToken.None));

        Assert.Equal(afterFirst, harness.Announcer.Announced.Count);
    }

    [Fact]
    public async Task A_failing_announcer_changes_neither_the_outcome_nor_the_answer()
    {
        KnowledgeIndexHarness succeeding = KnowledgeIndexHarness.Build();
        succeeding.Announcer.Failure = new InvalidOperationException("backplane down");

        Versioned<KnowledgeIndexResult>? result = await succeeding.IndexDetachedAsync();

        Assert.NotNull(result);
        Assert.Equal(KnowledgeIndexAttemptStatus.Succeeded, Assert.Single(succeeding.Attempts.Settled).Status);
        Assert.Equal(1, succeeding.Index.Replacements);

        KnowledgeIndexHarness failing = KnowledgeIndexHarness.Build();
        failing.Announcer.Failure = new InvalidOperationException("backplane down");
        failing.Embeddings.PretendUnavailable = true;

        // The caller sees the provider's failure, not the announcer's.
        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => failing.IndexDetachedAsync());
        Assert.Equal(KnowledgeIndexAttemptStatus.Failed, Assert.Single(failing.Attempts.Settled).Status);
    }

    [Fact]
    public void The_limits_sit_above_the_provider_work_by_their_headroom()
    {
        KnowledgeIndexingLimits limits = KnowledgeIndexingLimits.For(TimeSpan.FromMinutes(8));

        Assert.Equal(TimeSpan.FromMinutes(10), limits.AttemptTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeIndexingLimits(TimeSpan.Zero));
    }
}
