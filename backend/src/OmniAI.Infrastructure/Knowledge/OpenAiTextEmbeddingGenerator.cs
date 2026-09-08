using System.ClientModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using OmniAI.Application.Knowledge;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// The OpenAI embeddings adapter.
/// </summary>
/// <remarks>
/// <para>
/// The one file in the solution that names an embeddings type from a vendor SDK.
/// Everything above it sees <see cref="ITextEmbeddingGenerator"/>, a profile and
/// a validated vector.
/// </para>
/// <para>
/// <strong>Failures are translated, not leaked.</strong> A caller never sees a
/// <c>ClientResultException</c>, a status code or a provider message: it sees
/// "unavailable", "timed out" or "unusable answer", which are the three things it
/// can actually do something about. Nothing here logs the inputs, and the
/// credential never reaches a field, a log or an exception.
/// </para>
/// <para>
/// <strong>Zero retries, same as text generation.</strong> The SDK retries some
/// statuses by default; that default is disabled where the client is built. A
/// hidden retry turns one index into several bills and multiplies load during
/// exactly the incidents where that hurts most.
/// </para>
/// </remarks>
internal sealed partial class OpenAiTextEmbeddingGenerator : ITextEmbeddingGenerator
{
    private readonly EmbeddingClient _client;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OpenAiTextEmbeddingGenerator> _logger;

    public OpenAiTextEmbeddingGenerator(
        EmbeddingClient client,
        EmbeddingOptions options,
        EmbeddingProfile profile,
        ILogger<OpenAiTextEmbeddingGenerator> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public EmbeddingProfile Profile { get; }

    public async Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            // Answered without a request. An empty batch has a correct answer, and
            // paying a provider to agree is not it.
            return [];
        }

        if (inputs.Count > Profile.MaxBatchSize)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"At most {Profile.MaxBatchSize} inputs may be embedded in one request; {inputs.Count} were supplied. Nothing was sent: splitting the batch here would turn one caller's request into several billed calls without the caller knowing."),
                nameof(inputs));
        }

        foreach (string input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException(
                    "An input to embed must not be blank.", nameof(inputs));
            }

            if (input.Length > Profile.MaxInputCharacters)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"An input to embed must be at most {Profile.MaxInputCharacters} characters; one has {input.Length}."),
                    nameof(inputs));
            }
        }

        // Checked before anything is dispatched. The SDK pipeline does not
        // short-circuit an already-cancelled token, and with a paid provider
        // behind this, a caller who has already given up must not still be able to
        // spend money.
        cancellationToken.ThrowIfCancellationRequested();

        EmbeddingGenerationOptions options = new()
        {
            // Asked for explicitly rather than accepted as a default. The column
            // is declared for this width, and a provider that changed its default
            // would otherwise break indexing at the database rather than here.
            Dimensions = EmbeddingOptions.Dimensions,
        };

        ClientResult<OpenAIEmbeddingCollection> result;

        try
        {
            result = await _client
                .GenerateEmbeddingsAsync(inputs, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up. Not a provider failure, and reporting it as one
            // would turn a closed browser tab into an incident.
            throw;
        }
        catch (TaskCanceledException timeout)
        {
            LogUnavailable(_logger, "timeout", 0);

            throw new EmbeddingTimeoutException(
                "The embedding provider did not respond in time. Nothing was indexed.", timeout);
        }
        catch (ClientResultException failure)
        {
            throw Translate(failure);
        }

        return Read(result?.Value, inputs.Count);
    }

    /// <summary>Turns the provider's collection into validated vectors, in order.</summary>
    /// <remarks>
    /// Position is the only thing that pairs an input with its vector, so a
    /// response of the wrong length is refused rather than zipped as far as it
    /// goes: a short response would otherwise attach the wrong text to the wrong
    /// embedding for every item after the gap, and nothing downstream could tell.
    /// </remarks>
    private static EmbeddingVector[] Read(OpenAIEmbeddingCollection? embeddings, int expected)
    {
        if (embeddings is null || embeddings.Count != expected)
        {
            throw new EmbeddingResponseInvalidException(string.Create(
                CultureInfo.InvariantCulture,
                $"The embedding provider returned {embeddings?.Count ?? 0} vectors for {expected} inputs. Nothing was stored: pairing them up by position would attach the wrong vector to the wrong text."));
        }

        EmbeddingVector[] vectors = new EmbeddingVector[expected];

        for (int index = 0; index < expected; index++)
        {
            OpenAIEmbedding embedding = embeddings[index];

            if (embedding is null)
            {
                throw new EmbeddingResponseInvalidException();
            }

            vectors[index] = EmbeddingVector.Create(
                embedding.ToFloats().ToArray(),
                EmbeddingOptions.Dimensions);
        }

        return vectors;
    }

    private Exception Translate(ClientResultException failure)
    {
        int status = failure.Status;

        if (status is 408 or 504)
        {
            LogUnavailable(_logger, "upstream timeout", status);

            return new EmbeddingTimeoutException(
                "The embedding provider did not respond in time. Nothing was indexed.", failure);
        }

        string category = status switch
        {
            400 or 404 or 422 => "provider rejected the request",
            401 or 403 => "provider authentication or configuration",
            429 => "provider rate or capacity limit",
            >= 500 => "provider service failure",
            _ => "provider failure",
        };

        LogUnavailable(_logger, category, status);

        // Everything else becomes "the provider could not serve this". A 400
        // belongs here too: this platform validated the batch already, so the
        // provider disagreeing means the adapter or the configured model is wrong
        // -- an operator's problem, never something to blame on whoever wrote the
        // document.
        return new EmbeddingUnavailableException(
            "The embedding provider could not serve this request. Nothing was indexed and no "
            + "content was changed.",
            failure);
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "OpenAI embedding failed ({Category}, status {StatusCode}). Nothing was indexed.")]
    private static partial void LogUnavailable(ILogger logger, string category, int statusCode);
}
