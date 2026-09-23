using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.WhatsApp;

namespace OmniAI.Infrastructure.IntegrationTests.WhatsApp;

/// <summary>
/// The Development guard against a WhatsApp message nobody meant to send.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The OpenAI guard exists because a key was inherited once and cost
/// money.</strong> This one exists because the same accident on this channel is
/// worse: a machine pointed at Graph, with a token still in its configuration,
/// puts words into a real person's WhatsApp, and there is no refund for that.
/// </para>
/// <para>
/// <strong>Nothing here reaches a network.</strong> The named client's innermost
/// handler is replaced with one that records and then fails the test if it is
/// ever reached, so "zero requests" is asserted rather than assumed. No Meta
/// account, no WABA, no real credential and no real phone number appears in this
/// file.
/// </para>
/// </remarks>
public sealed class MetaLiveProviderOptInTests
{
    /// <summary>A host that is deliberately Meta's, and deliberately never called.</summary>
    private const string RealGraph = "https://" + WhatsAppTransportOptions.MetaGraphHost + "/";

    /// <summary>A stub on this machine, as local verification uses.</summary>
    private const string LocalStub = "http://127.0.0.1:4243/";

    private const string Token = "fake-local-test-token";

    private static readonly PhoneNumberId From = PhoneNumberId.Create("100200300400500");

    private static readonly WhatsAppCustomerId To = WhatsAppCustomerId.Create("447700900123");

    // ---------------------------------------------------------------- A and B

    [Fact]
    public async Task Development_against_the_real_provider_without_the_opt_in_sends_nothing()
    {
        // (A) A credential that looks perfectly usable, a real Graph host, and
        // nobody having said this process may talk to it.
        using Harness harness = Harness.Build(development: true, address: RealGraph);

        WhatsAppSendResult result = await harness.SendAsync();

        Assert.Equal(WhatsAppSendKind.Rejected, result.Kind);

        // The whole point: not one request left the process.
        Assert.Empty(harness.Handler.Requests);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1 ")]
    [InlineData("01")]
    public async Task Development_against_the_real_provider_with_anything_but_one_sends_nothing(
        string optIn)
    {
        // (B) The guard's vocabulary is one value. "true" and "yes" are refused
        // deliberately: a guard that accepts whatever looks affirmative is one
        // somebody trips over by accident, which is the failure it exists for.
        using Harness harness = Harness.Build(development: true, address: RealGraph, optIn: optIn);

        Assert.Equal(WhatsAppSendKind.Rejected, (await harness.SendAsync()).Kind);

        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task The_refusal_names_the_variable_and_carries_no_credential()
    {
        // It is stored on the delivery row and shown in the inbox, so it has to
        // survive being read by somebody who was not expecting it -- and it must
        // not smuggle the token into a place a screenshot can reach.
        using Harness harness = Harness.Build(development: true, address: RealGraph);

        string reason = (await harness.SendAsync()).Reason ?? string.Empty;

        Assert.Contains(
            WhatsAppTransportOptions.LiveProviderOptInVariable,
            reason,
            StringComparison.Ordinal);

        Assert.DoesNotContain(Token, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(To.Value, reason, StringComparison.Ordinal);
        Assert.DoesNotContain(WhatsAppTransportOptions.MetaGraphHost, reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_withheld_send_is_a_refusal_rather_than_an_unknown()
    {
        // Ambiguous would be a lie. The message provably did not leave the
        // process, and calling it unknown would leave a durable row nobody can
        // ever settle -- the guard must not make the stored state less truthful
        // than a real refusal would.
        using Harness harness = Harness.Build(development: true, address: RealGraph);

        WhatsAppSendResult result = await harness.SendAsync();

        Assert.NotEqual(WhatsAppSendKind.Ambiguous, result.Kind);
        Assert.Null(result.ProviderMessageId);
    }

    // ---------------------------------------------------------------------- C

    [Fact]
    public async Task Development_against_a_local_stub_works_without_any_opt_in()
    {
        // (C) The whole of local verification -- signed webhooks, the inbox,
        // handoff, delivery receipts -- must keep working on a laptop where
        // nobody has set anything. A guard that made the stub require a flag
        // would be a guard everybody switches on permanently.
        using Harness harness = Harness.Build(development: true, address: LocalStub);

        Assert.IsType<MetaWhatsAppMessageSender>(harness.Sender);

        await harness.SendAsync();

        HttpRequestMessage request = Assert.Single(harness.Handler.Requests);

        Assert.StartsWith(LocalStub, request.RequestUri?.ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------- D

    [Fact]
    public void Production_against_the_real_provider_needs_no_development_opt_in()
    {
        // (D) A deployment that has deliberately configured the channel must not
        // go silent because somebody forgot a flag. This is a cost-and-accident
        // guard for developer machines, not a security control for deployments.
        using Harness harness = Harness.Build(development: false, address: RealGraph);

        Assert.IsType<MetaWhatsAppMessageSender>(harness.Sender);
    }

    [Fact]
    public void Production_ignores_the_development_opt_in_entirely()
    {
        foreach (string? optIn in new string?[] { null, "0", "false", "1" })
        {
            using Harness harness = Harness.Build(development: false, address: RealGraph, optIn: optIn);

            Assert.IsType<MetaWhatsAppMessageSender>(harness.Sender);
        }
    }

    // ------------------------------------------------------------ opted in

    [Fact]
    public void Development_with_the_opt_in_composes_the_real_transport()
    {
        // Deliberate, and only then. Composed here; nothing in this test calls
        // it, so no request is made.
        using Harness harness = Harness.Build(
            development: true,
            address: RealGraph,
            optIn: WhatsAppTransportOptions.LiveProviderOptInValue);

        Assert.IsType<MetaWhatsAppMessageSender>(harness.Sender);
    }

    [Fact]
    public void A_non_loopback_address_that_is_not_meta_is_still_treated_as_live()
    {
        // The guard asks "is this on my machine?", not "is this Meta?". A host
        // allow-list would let every typo, regional alias and proxy through on
        // the permitted side.
        using Harness harness = Harness.Build(development: true, address: "https://graph.example.test/");

        Assert.IsType<LiveMetaDisabledMessageSender>(harness.Sender);
    }

    // ------------------------------------------------------------- readiness

    [Fact]
    public void The_readiness_facts_match_what_was_actually_composed()
    {
        // The preflight must not be able to report that sending works while every
        // send is being refused.
        using Harness withheld = Harness.Build(development: true, address: RealGraph);

        WhatsAppProviderReadiness guarded = withheld.Readiness.Describe();

        Assert.True(guarded.TargetsLiveProvider);
        Assert.False(guarded.LiveSendingAllowed);
        Assert.True(guarded.AddressIsUsable);
        Assert.True(guarded.VersionIsUsable);

        using Harness stub = Harness.Build(development: true, address: LocalStub);

        WhatsAppProviderReadiness local = stub.Readiness.Describe();

        Assert.False(local.TargetsLiveProvider);
        Assert.True(local.LiveSendingAllowed);
    }

    [Fact]
    public void The_readiness_facts_disclose_no_configuration()
    {
        // Four booleans and a version. Not the host somebody configured.
        using Harness harness = Harness.Build(development: false, address: RealGraph);

        WhatsAppProviderReadiness facts = harness.Readiness.Describe();

        Assert.Equal(WhatsAppTransportOptions.DefaultGraphApiVersion, facts.ApiVersion);

        Assert.DoesNotContain(
            WhatsAppTransportOptions.MetaGraphHost,
            facts.ToString(),
            StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- helpers

    /// <summary>The WhatsApp channel, composed exactly as the API composes it.</summary>
    private sealed class Harness : IDisposable
    {
        private Harness(ServiceProvider provider, RecordingHandler handler)
        {
            Provider = provider;
            Handler = handler;
            Scope = provider.CreateScope();
        }

        private ServiceProvider Provider { get; }

        private IServiceScope Scope { get; }

        public RecordingHandler Handler { get; }

        public IWhatsAppMessageSender Sender =>
            Scope.ServiceProvider.GetRequiredService<IWhatsAppMessageSender>();

        public IWhatsAppProviderReadiness Readiness =>
            Provider.GetRequiredService<IWhatsAppProviderReadiness>();

        public Task<WhatsAppSendResult> SendAsync() =>
            Sender.SendTextAsync(From, To, "Hello", Token);

        public static Harness Build(bool development, string address, string? optIn = null)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>(
                        $"{WhatsAppTransportOptions.SectionName}:GraphBaseAddress",
                        address),
                    new KeyValuePair<string, string?>(
                        WhatsAppTransportOptions.LiveProviderOptInVariable,
                        optIn),
                ])
                .Build();

            ServiceCollection services = [];

            services.AddLogging();

            // Exactly what the composition root passes: Development, and nothing
            // else.
            services.AddOmniAiWhatsApp(configuration, requireLiveProviderOptIn: development);

            RecordingHandler handler = new();

            // Replaces the innermost handler of the named client the sender
            // resolves, so a request that is made is recorded and a request that
            // is not made cannot quietly have happened somewhere else.
            services
                .AddHttpClient(WhatsAppTransportOptions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            return new Harness(services.BuildServiceProvider(), handler);
        }

        public void Dispose()
        {
            Scope.Dispose();
            Provider.Dispose();
        }
    }

    /// <summary>A transport that answers from memory and remembers being asked.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"messages":[{"id":"wamid.LOCAL=="}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
