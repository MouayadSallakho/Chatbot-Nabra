using System.Net;
using OmniAI.Application.Ai;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// How provider failures cross the Infrastructure boundary.
/// </summary>
/// <remarks>
/// Every case is driven by a canned HTTP status and a real OpenAI error body, so
/// what is asserted is the adapter's translation of what the provider actually
/// sends — including that none of that body reaches the caller.
/// </remarks>
public sealed class OpenAiFailureTests
{
    /// <summary>Text planted in every canned error body that must never escape.</summary>
    private const string LeakProbe = "sk-leak-probe-should-never-surface";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_authentication_failure_becomes_runtime_unavailable(HttpStatusCode status)
    {
        // A rejected key is an operator's problem, not the caller's, and the
        // caller must not be able to learn which it was.
        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(() => FailWith(status));
    }

    [Fact]
    public async Task A_rate_limit_becomes_runtime_unavailable()
    {
        // 429 and a rejected key are reported identically to a client on purpose:
        // both mean "this deployment cannot serve you right now", and the
        // difference is an operational detail that belongs in the log.
        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => FailWith(HttpStatusCode.TooManyRequests));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task An_upstream_server_failure_becomes_runtime_unavailable(HttpStatusCode status)
    {
        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(() => FailWith(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task An_upstream_timeout_becomes_a_timeout(HttpStatusCode status)
    {
        // Distinct from unavailable, because it tells the caller something
        // different: the work probably started and the answer did not arrive.
        await Assert.ThrowsAsync<AiRuntimeTimeoutException>(() => FailWith(status));
    }

    [Fact]
    public async Task A_transport_timeout_becomes_a_timeout()
    {
        // Not an HTTP status at all: the transport's own deadline expiring, which
        // surfaces as a cancellation that did not come from the caller's token.
        CannedOpenAiTransport transport = new()
        {
            ThrowInstead = () => new TaskCanceledException("The request timed out."),
        };

        await Assert.ThrowsAsync<AiRuntimeTimeoutException>(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));
    }

    [Fact]
    public async Task A_provider_rejection_becomes_runtime_unavailable()
    {
        // A 400 from the provider is not the caller's fault: this platform already
        // validated the message, so the provider disagreeing means the adapter or
        // the configured model is wrong. Blaming whoever typed the message would
        // send them to fix the one thing that was not broken.
        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => FailWith(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task No_provider_exception_type_ever_escapes()
    {
        // ClientResultException is the SDK's own type. If one reached a use case,
        // the Application would be handling a vendor's exception and the whole
        // abstraction would be decorative.
        CannedOpenAiTransport transport = Failing(HttpStatusCode.InternalServerError);

        Exception failure = await Record.ExceptionAsync(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));

        Assert.NotNull(failure);
        Assert.Equal("OmniAI.Application", failure.GetType().Assembly.GetName().Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task No_provider_detail_reaches_the_failure_message(HttpStatusCode status)
    {
        Exception failure = await Record.ExceptionAsync(
            () => OpenAiTestHarness.Generator(Failing(status)).GenerateAsync(OpenAiTestHarness.SimpleRequest()));

        Assert.NotNull(failure);

        // The message is what a client can be shown. Nothing about the vendor,
        // the credential, the request id or the upstream body may be in it.
        foreach (string forbidden in (string[])
            [LeakProbe, "openai", "req_canned", "invalid_request_error", "sk-",
             "api.openai.com", "Authorization", "bearer", OpenAiTestHarness.DummyApiKey])
        {
            Assert.DoesNotContain(forbidden, failure.Message, StringComparison.OrdinalIgnoreCase);
        }

        // The status code is not disclosed either: it would tell an
        // unauthenticated caller whether this deployment's key is valid.
        Assert.DoesNotContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture),
            failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task The_sdk_does_not_retry_a_failure_it_would_retry_by_default(HttpStatusCode status)
    {
        // The load-bearing test for the retry decision. The official SDK retries
        // 408, 429 and 5xx by default; every status here is on that list. Exactly
        // one HTTP call must be made.
        //
        // Without RetryPolicy = ClientRetryPolicy(0) this would see several calls,
        // meaning one preview quietly became several billed requests and an
        // "unavailable" answer arrived after several timeouts rather than one.
        CannedOpenAiTransport transport = Failing(status);

        await Record.ExceptionAsync(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));

        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Nothing_is_retried_after_an_unusable_response_either()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = OpenAiCannedResponses.NoOutput() };

        await Assert.ThrowsAsync<AiResponseInvalidException>(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));

        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_stays_cancellation()
    {
        // A closed browser tab is not an upstream fault. Reporting it as one would
        // make an ordinary event look like an incident and would corrupt any
        // latency signal built on these failures.
        CannedOpenAiTransport transport = new() { ObserveCancellation = true };

        using CancellationTokenSource source = new();

        Task<AiTextGenerationResult> pending = OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), source.Token);

        await source.CancelAsync();

        Exception failure = await Record.ExceptionAsync(() => pending);

        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.IsNotType<AiRuntimeTimeoutException>(failure);
        Assert.IsNotType<AiRuntimeUnavailableException>(failure);
    }

    [Fact]
    public async Task An_already_cancelled_request_never_reaches_the_provider()
    {
        CannedOpenAiTransport transport = new();

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OpenAiTestHarness.Generator(transport)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), source.Token));

        Assert.Equal(0, transport.Calls);
    }

    // ------------------------------------------------------------------- helpers

    private static CannedOpenAiTransport Failing(HttpStatusCode status) => new()
    {
        Status = status,
        ResponseBody = OpenAiCannedResponses.Error(
            "test_error",
            $"OpenAI rejected this request. Credential {LeakProbe} at api.openai.com."),
    };

    private static Task<AiTextGenerationResult> FailWith(HttpStatusCode status) =>
        OpenAiTestHarness.Generator(Failing(status)).GenerateAsync(OpenAiTestHarness.SimpleRequest());
}
