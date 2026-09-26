using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// A canned OpenAI embeddings socket: deterministic vectors, deliberate latency.
/// </summary>
/// <remarks>
/// <para>
/// Installed under the real <c>EmbeddingClient</c> and the real
/// <c>OpenAiTextEmbeddingGenerator</c>, so indexing still commits its attempt and
/// ledger row before this is reached and settles them afterwards.
/// </para>
/// <para>
/// The vector is a hashed bag of words, normalised: texts that share words point
/// the same way, so a question about "refunds" really is nearest to the chunk
/// about refunds. Deterministic, so the same text always gets the same vector.
/// </para>
/// </remarks>
internal sealed class LatencyEmbeddingHandler : HttpMessageHandler
{
    private const int Dimensions = 1536;

    private int _calls;
    private int _inFlight;
    private int _maxInFlight;
    private int _inputs;

    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(150);

    public int Calls => Volatile.Read(ref _calls);

    public int Inputs => Volatile.Read(ref _inputs);

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public void Reset()
    {
        Interlocked.Exchange(ref _calls, 0);
        Interlocked.Exchange(ref _inFlight, 0);
        Interlocked.Exchange(ref _maxInFlight, 0);
        Interlocked.Exchange(ref _inputs, 0);
        Latency = TimeSpan.FromMilliseconds(150);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Interlocked.Increment(ref _calls);
        int now = Interlocked.Increment(ref _inFlight);
        int observed;

        do
        {
            observed = Volatile.Read(ref _maxInFlight);
        }
        while (now > observed && Interlocked.CompareExchange(ref _maxInFlight, now, observed) != observed);

        try
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument parsed = JsonDocument.Parse(body);
            JsonElement input = parsed.RootElement.GetProperty("input");
            List<string> texts = input.ValueKind == JsonValueKind.Array
                ? [.. input.EnumerateArray().Select(item => item.GetString() ?? string.Empty)]
                : [input.GetString() ?? string.Empty];

            Interlocked.Add(ref _inputs, texts.Count);

            await Task.Delay(Latency, cancellationToken).ConfigureAwait(false);

            StringBuilder json = new("{\"object\":\"list\",\"data\":[");

            for (int i = 0; i < texts.Count; i++)
            {
                json.Append(i == 0 ? string.Empty : ",")
                    .Append("{\"object\":\"embedding\",\"index\":").Append(i).Append(",\"embedding\":[");

                float[] vector = Vector(texts[i]);

                for (int d = 0; d < vector.Length; d++)
                {
                    json.Append(d == 0 ? string.Empty : ",").Append(vector[d].ToString("R", CultureInfo.InvariantCulture));
                }

                json.Append("]}");
            }

            int tokens = texts.Sum(t => Math.Max(1, t.Length / 4));
            json.Append("],\"model\":\"text-embedding-3-small\",\"usage\":{\"prompt_tokens\":")
                .Append(tokens).Append(",\"total_tokens\":").Append(tokens).Append("}}");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json"),
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static float[] Vector(string text)
    {
        float[] vector = new float[Dimensions];

        foreach (string word in text.ToUpperInvariant().Split(
            [' ', '.', ',', '?', '!', '\n', '\r', ':', ';'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            int hash = 17;

            foreach (char c in word)
            {
                hash = unchecked((hash * 31) + c);
            }

            vector[(hash & int.MaxValue) % Dimensions] += 1f;
        }

        double norm = Math.Sqrt(vector.Sum(v => (double)v * v));

        if (norm == 0)
        {
            vector[0] = 1f;

            return vector;
        }

        for (int d = 0; d < Dimensions; d++)
        {
            vector[d] = (float)(vector[d] / norm);
        }

        return vector;
    }
}
