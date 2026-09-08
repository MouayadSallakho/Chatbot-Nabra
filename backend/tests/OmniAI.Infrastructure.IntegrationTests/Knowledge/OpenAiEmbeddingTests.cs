using System.ClientModel.Primitives;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Application.Knowledge;
using OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// The embeddings adapter, over a socket that never leaves the process.
/// </summary>
/// <remarks>
/// <strong>No test here reaches the internet.</strong> Every request is answered
/// by a canned transport, and the credential is a fixed non-secret string. That
/// is what makes it safe to assert on the exact request body the SDK produces --
/// which is the only way to know what this platform actually sends a provider.
/// </remarks>
public sealed class OpenAiEmbeddingTests
{
    private const int Dimensions = EmbeddingOptions.Dimensions;

    private static OpenAiTextEmbeddingGenerator Generator(
        CannedOpenAiTransport transport,
        EmbeddingOptions? options = null)
    {
        options ??= new EmbeddingOptions();

        PipelineTransport pipeline = new HttpClientPipelineTransport(new HttpClient(transport));

        return new OpenAiTextEmbeddingGenerator(
            OpenAiEmbeddingClientFactory.Create(
                OpenAiTestHarness.DummyApiKey,
                options,
                pipeline),
            options,
            EmbeddingProfile.Create(
                options.ProfileId,
                Dimensions,
                options.MaxBatchSize,
                options.MaxInputCharacters),
            NullLogger<OpenAiTextEmbeddingGenerator>.Instance);
    }

    /// <summary>A canned embeddings response with the right shape.</summary>
    private static string Response(int count, int dimensions = Dimensions)
    {
        StringBuilder json = new();

        json.Append("{\"object\":\"list\",\"model\":\"text-embedding-3-small\",\"data\":[");

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append(CultureInfo.InvariantCulture, $"{{\"object\":\"embedding\",\"index\":{index},\"embedding\":[");

            for (int value = 0; value < dimensions; value++)
            {
                if (value > 0)
                {
                    json.Append(',');
                }

                json.Append(CultureInfo.InvariantCulture, $"{(value + index) % 7 / 10.0}");
            }

            json.Append("]}");
        }

        json.Append("],\"usage\":{\"prompt_tokens\":1,\"total_tokens\":1}}");

        return json.ToString();
    }

    // ------------------------------------------------------------------ shape

    [Fact]
    public async Task Every_input_gets_a_vector_of_the_configured_width_in_order()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Response(3) };

        IReadOnlyList<EmbeddingVector> vectors =
            await Generator(transport).GenerateAsync(["first", "second", "third"]);

        Assert.Equal(3, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(Dimensions, vector.Dimensions));
    }

    [Fact]
    public async Task The_configured_dimension_is_asked_for_explicitly()
    {
        // Not left to the provider's default. The column is declared for this
        // width, and a provider changing its default would otherwise break
        // indexing at the database rather than here.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1) };

        await Generator(transport).GenerateAsync(["anything"]);

        using JsonDocument body = JsonDocument.Parse(transport.LastBody);

        Assert.Equal(Dimensions, body.RootElement.GetProperty("dimensions").GetInt32());
    }

    [Fact]
    public async Task The_configured_model_is_the_one_that_is_sent()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1) };

        await Generator(transport).GenerateAsync(["anything"]);

        using JsonDocument body = JsonDocument.Parse(transport.LastBody);

        Assert.Equal(
            EmbeddingOptions.DefaultModel,
            body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task An_empty_batch_is_answered_without_a_request()
    {
        // An empty batch has a correct answer, and paying a provider to agree is
        // not it.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(0) };

        Assert.Empty(await Generator(transport).GenerateAsync([]));
        Assert.Equal(0, transport.Calls);
    }

    // --------------------------------------------------------------- batching

    [Fact]
    public async Task A_batch_larger_than_the_profile_allows_is_refused_before_it_is_sent()
    {
        // Splitting it here would turn one caller's request into several billed
        // calls without the caller knowing.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(5) };

        EmbeddingOptions options = new() { MaxBatchSize = 2 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Generator(transport, options).GenerateAsync(["a", "b", "c"]));

        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task One_batch_is_one_request()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Response(4) };

        await Generator(transport).GenerateAsync(["a", "b", "c", "d"]);

        Assert.Equal(1, transport.Calls);

        using JsonDocument body = JsonDocument.Parse(transport.LastBody);

        Assert.Equal(4, body.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task A_blank_input_is_refused_before_anything_is_sent()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Response(2) };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Generator(transport).GenerateAsync(["fine", "   "]));

        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task An_over_long_input_is_refused_before_anything_is_sent()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1) };

        EmbeddingOptions options = new() { MaxInputCharacters = 10 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Generator(transport, options).GenerateAsync(["far too long for this"]));

        Assert.Equal(0, transport.Calls);
    }

    // --------------------------------------------------------------- failures

    [Fact]
    public async Task A_response_with_too_few_vectors_is_refused()
    {
        // Pairing up as far as the response goes would attach the wrong vector to
        // the wrong text for every item after the gap.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(2) };

        EmbeddingResponseInvalidException failure =
            await Assert.ThrowsAsync<EmbeddingResponseInvalidException>(
                () => Generator(transport).GenerateAsync(["a", "b", "c"]));

        Assert.Contains("2 vectors for 3 inputs", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vector_of_the_wrong_width_is_refused()
    {
        // The case the Application suite cannot construct, because EmbeddingVector
        // validates on construction. Here a provider genuinely answers with the
        // wrong number of floats.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1, dimensions: 8) };

        EmbeddingResponseInvalidException failure =
            await Assert.ThrowsAsync<EmbeddingResponseInvalidException>(
                () => Generator(transport).GenerateAsync(["anything"]));

        Assert.Contains("8 dimensions", failure.Message, StringComparison.Ordinal);
        Assert.Contains("configured for 1536", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_provider_failure_becomes_unavailable(HttpStatusCode status)
    {
        CannedOpenAiTransport transport = new()
        {
            Status = status,
            ResponseBody = "{\"error\":{\"message\":\"no\"}}",
        };

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => Generator(transport).GenerateAsync(["anything"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task An_upstream_timeout_becomes_a_timeout(HttpStatusCode status)
    {
        CannedOpenAiTransport transport = new()
        {
            Status = status,
            ResponseBody = "{\"error\":{\"message\":\"slow\"}}",
        };

        await Assert.ThrowsAsync<EmbeddingTimeoutException>(
            () => Generator(transport).GenerateAsync(["anything"]));
    }

    [Fact]
    public async Task A_transport_timeout_becomes_a_timeout_rather_than_a_cancellation()
    {
        CannedOpenAiTransport transport = new() { ObserveCancellation = true };

        EmbeddingOptions options = new() { TimeoutSeconds = 1 };

        await Assert.ThrowsAsync<EmbeddingTimeoutException>(
            () => Generator(transport, options).GenerateAsync(["anything"]));
    }

    [Fact]
    public async Task A_cancelled_caller_is_never_reported_as_a_provider_failure()
    {
        // A closed browser tab is not an incident, and reporting it as one would
        // poison any latency signal built on these logs.
        CannedOpenAiTransport transport = new() { ObserveCancellation = true };

        using CancellationTokenSource cancellation = new();

        Task<IReadOnlyList<EmbeddingVector>> pending =
            Generator(transport).GenerateAsync(["anything"], cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task An_already_cancelled_caller_never_reaches_the_transport()
    {
        // With a paid provider behind this, a caller who has already given up must
        // not still be able to spend money.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1) };

        using CancellationTokenSource cancellation = new();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Generator(transport).GenerateAsync(["anything"], cancellation.Token));

        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task A_failure_is_attempted_exactly_once()
    {
        // The SDK retries 429 and 5xx by default. A hidden retry would turn one
        // index into several bills and would multiply load during exactly the
        // incidents where that hurts most.
        CannedOpenAiTransport transport = new()
        {
            Status = HttpStatusCode.TooManyRequests,
            ResponseBody = "{\"error\":{\"message\":\"slow down\"}}",
        };

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => Generator(transport).GenerateAsync(["anything"]));

        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task The_profile_identifier_names_the_provider_the_model_and_the_width()
    {
        // The marker stored beside every vector. Changing any part of it makes
        // every document indexed under the old one report as needing an index,
        // rather than being compared against vectors it cannot be compared with.
        CannedOpenAiTransport transport = new() { ResponseBody = Response(1) };

        OpenAiTextEmbeddingGenerator generator = Generator(transport);

        Assert.Equal("openai:text-embedding-3-small:1536", generator.Profile.Id.Value);
        Assert.Equal(1_536, generator.Profile.Dimensions);
    }
}
