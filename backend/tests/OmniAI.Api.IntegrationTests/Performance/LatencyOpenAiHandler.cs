using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// A canned OpenAI socket that takes time to answer, and watches for overlap.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The seam is the socket, not the generator.</strong> Installed under the
/// real <c>ResponsesClient</c> and the real <c>OpenAiTextGenerator</c>, so every
/// call still goes through the production cost-safety path: the ledger row is
/// committed before this handler is reached and settled after it answers. No
/// network, no key, no cost.
/// </para>
/// <para>
/// <strong>The latency is deliberate.</strong> An instantaneous stub would make
/// "two generations for one conversation overlapped" unobservable: they would never
/// be in flight at the same moment whether or not they were serialised. A delay
/// long enough to overlap is what turns the lock into something a test can see.
/// </para>
/// <para>
/// Each customer message carries a marker (<c>[conv:KEY]</c>) naming the scenario's
/// conversation, and the handler reads it out of the outgoing request body to keep
/// a per-conversation in-flight count. That is the observation the
/// same-conversation invariant is asserted on.
/// </para>
/// </remarks>
internal sealed partial class LatencyOpenAiHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, int> _inFlightByConversation = new();
    private readonly ConcurrentDictionary<string, int> _maxByConversation = new();
    private readonly ConcurrentBag<string> _instructions = [];
    private readonly ConcurrentBag<string> _bodies = [];
    private readonly Lock _sync = new();

    private int _inFlight;
    private int _maxInFlight;
    private int _calls;

    /// <summary>How long each answer takes.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// When true, the handler waits out its latency and then fails like a
    /// transport timeout, which the adapter must record as Unknown.
    /// </summary>
    public bool SimulateTimeout { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    /// <summary>Largest number of simultaneous calls seen for one conversation marker.</summary>
    public int MaxInFlightFor(string conversationKey) =>
        _maxByConversation.TryGetValue(conversationKey, out int max) ? max : 0;

    public int MaxInFlightForAnyConversation =>
        _maxByConversation.IsEmpty ? 0 : _maxByConversation.Values.Max();

    /// <summary>The agent instructions each call carried (for isolation checks).</summary>
    public IReadOnlyCollection<string> Instructions => [.. _instructions];

    /// <summary>Every request body sent to the provider (grounding included).</summary>
    public IReadOnlyCollection<string> Bodies => [.. _bodies];

    public void Reset()
    {
        lock (_sync)
        {
            _inFlightByConversation.Clear();
            _maxByConversation.Clear();
            _instructions.Clear();
            _bodies.Clear();
            Interlocked.Exchange(ref _inFlight, 0);
            Interlocked.Exchange(ref _maxInFlight, 0);
            Interlocked.Exchange(ref _calls, 0);
            Latency = TimeSpan.FromMilliseconds(200);
            SimulateTimeout = false;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Interlocked.Increment(ref _calls);

        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // The last marker in the body is the newest customer turn; earlier turns of
        // the same conversation repeat the same key, so any match identifies it.
        MatchCollection markers = Marker().Matches(body);
        string? key = markers.Count == 0 ? null : markers[^1].Groups[1].Value;

        _bodies.Add(body);

        Match instructions = InstructionsField().Match(body);

        if (instructions.Success)
        {
            _instructions.Add(instructions.Groups[1].Value);
        }

        int now = Interlocked.Increment(ref _inFlight);
        RaiseMax(ref _maxInFlight, now);

        if (key is not null)
        {
            int forConversation = _inFlightByConversation.AddOrUpdate(key, 1, (_, value) => value + 1);
            _maxByConversation.AddOrUpdate(key, forConversation, (_, value) => Math.Max(value, forConversation));
        }

        try
        {
            await Task.Delay(Latency, cancellationToken).ConfigureAwait(false);

            if (SimulateTimeout)
            {
                // Not the caller's token: this is how a transport timeout surfaces.
                throw new TaskCanceledException("Simulated provider timeout.");
            }

            string text = key is null ? "Certainly." : $"Reply for {key}.";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Completed(text), Encoding.UTF8, "application/json"),
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);

            if (key is not null)
            {
                _inFlightByConversation.AddOrUpdate(key, 0, (_, value) => value - 1);
            }
        }
    }

    private static void RaiseMax(ref int target, int candidate)
    {
        int observed;

        do
        {
            observed = Volatile.Read(ref target);

            if (candidate <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, observed) != observed);
    }

    private static string Completed(string text) =>
        $$"""
        {
          "id": "resp_{{Guid.NewGuid():N}}", "object": "response", "created_at": 1730000000,
          "status": "completed", "model": "gpt-canned",
          "output": [ { "type": "message", "id": "m1", "role": "assistant", "status": "completed",
            "content": [ { "type": "output_text", "text": "{{text}}", "annotations": [] } ] } ],
          "usage": { "input_tokens": 40, "output_tokens": 8, "total_tokens": 48 }
        }
        """;

    [GeneratedRegex(@"\[conv:([A-Za-z0-9-]+)\]")]
    private static partial Regex Marker();

    [GeneratedRegex("\"instructions\"\\s*:\\s*\"([^\"]*)\"")]
    private static partial Regex InstructionsField();
}
