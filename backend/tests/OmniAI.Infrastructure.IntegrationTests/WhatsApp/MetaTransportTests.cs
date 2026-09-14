using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.WhatsApp;

namespace OmniAI.Infrastructure.IntegrationTests.WhatsApp;

/// <summary>
/// The Meta HTTP adapter, against a handler that never reaches a network.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here contacts Meta.</strong> The client is built the way
/// production builds it and its innermost handler is replaced with one that
/// answers from memory, so the request this platform would actually put on the
/// wire is inspected byte for byte without a single packet leaving the machine.
/// </para>
/// <para>
/// The access token is an invented string. There is no Meta account, no WABA and
/// no real credential anywhere in this suite.
/// </para>
/// </remarks>
public sealed class MetaTransportTests
{
    private const string Token = "fake-local-test-token";

    private static readonly PhoneNumberId From = PhoneNumberId.Create("100200300400500");

    private static readonly WhatsAppCustomerId To = WhatsAppCustomerId.Create("447700900123");

    [Fact]
    public async Task A_send_goes_to_the_pinned_version_and_the_connections_own_number()
    {
        Transport transport = Transport.Answering(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.ACCEPTED=="}]}""");

        await transport.Sender.SendTextAsync(From, To, "Hello", Token);

        HttpRequestMessage request = Assert.Single(transport.Handler.Requests);

        // The version is configuration, not a literal spread through the code.
        // Meta's unversioned paths follow whatever is current, which would change
        // this platform's behaviour on a date somebody else picked.
        Assert.Equal(
            $"https://graph.local.test/{WhatsAppTransportOptions.DefaultGraphApiVersion}"
            + "/100200300400500/messages",
            request.RequestUri?.ToString());

        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task The_credential_travels_as_a_bearer_token_on_the_request_only()
    {
        Transport transport = Transport.Answering(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.ACCEPTED=="}]}""");

        await transport.Sender.SendTextAsync(From, To, "Hello", Token);

        HttpRequestMessage request = Assert.Single(transport.Handler.Requests);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Token, request.Headers.Authorization?.Parameter);

        // Never on the shared client, which serves every workspace. A default
        // header would send one workspace's credential with another's message.
        Assert.Null(transport.Client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task The_body_is_the_documented_text_envelope()
    {
        Transport transport = Transport.Answering(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.ACCEPTED=="}]}""");

        await transport.Sender.SendTextAsync(From, To, "Hello there", Token);

        using JsonDocument body = JsonDocument.Parse(transport.Handler.Bodies[0]);

        JsonElement root = body.RootElement;

        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("447700900123", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Hello there", root.GetProperty("text").GetProperty("body").GetString());

        // Link previews off. They make Meta fetch whatever a reply links to,
        // which turns a message into an outbound request nobody reviewed.
        Assert.False(root.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public async Task An_accepted_send_returns_the_provider_message_id()
    {
        Transport transport = Transport.Answering(
            HttpStatusCode.OK,
            """{"messages":[{"id":"wamid.ACCEPTED=="}]}""");

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        Assert.Equal(WhatsAppSendKind.Accepted, result.Kind);
        Assert.Equal("wamid.ACCEPTED==", result.ProviderMessageId?.Value);
    }

    [Fact]
    public async Task A_success_with_no_message_id_is_accepted_without_one()
    {
        Transport transport = Transport.Answering(HttpStatusCode.OK, """{"messages":[]}""");

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        Assert.Equal(WhatsAppSendKind.Accepted, result.Kind);
        Assert.Null(result.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_client_error_is_a_definitive_refusal(HttpStatusCode status)
    {
        Transport transport = Transport.Answering(
            status,
            """{"error":{"message":"Recipient not in allowed list","code":131030,"fbtrace_id":"Abc123"}}""");

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        Assert.Equal(WhatsAppSendKind.Rejected, result.Kind);

        // The reason is built from named fields, never from the raw body: Meta's
        // error payload can echo the request, and this string is stored and shown
        // in the inbox.
        Assert.Contains("131030", result.Reason!, StringComparison.Ordinal);
        Assert.Contains("Abc123", result.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("fbtrace_id", result.Reason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_server_error_or_timeout_status_is_ambiguous(HttpStatusCode status)
    {
        Transport transport = Transport.Answering(status, "{}");

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        // It may have been raised after the message was queued. Recording it as
        // failed would invite a retry that shows the customer two copies.
        Assert.Equal(WhatsAppSendKind.Ambiguous, result.Kind);
    }

    [Fact]
    public async Task A_dropped_connection_is_ambiguous_and_is_not_retried()
    {
        Transport transport = Transport.Failing(new HttpRequestException("connection reset"));

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        Assert.Equal(WhatsAppSendKind.Ambiguous, result.Kind);

        // <strong>Exactly one attempt.</strong> Nothing in the client pipeline
        // retries: a hidden retry is how one ambiguous send becomes three.
        Assert.Single(transport.Handler.Requests);
    }

    [Fact]
    public async Task A_timeout_is_ambiguous_and_is_not_retried()
    {
        Transport transport = Transport.Failing(
            new TaskCanceledException("timed out", new TimeoutException()));

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        Assert.Equal(WhatsAppSendKind.Ambiguous, result.Kind);
        Assert.Single(transport.Handler.Requests);
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_propagates()
    {
        Transport transport = Transport.Answering(HttpStatusCode.OK, "{}");

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.Sender.SendTextAsync(From, To, "Hello", Token, cancelled.Token));
    }

    [Fact]
    public async Task No_response_text_ever_carries_the_credential()
    {
        Transport transport = Transport.Answering(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"bad","code":1}}""");

        WhatsAppSendResult result = await transport.Sender
            .SendTextAsync(From, To, "Hello", Token);

        // The reason is stored in the database and rendered in the admin. A
        // credential reaching it would be a token in a screenshot.
        Assert.DoesNotContain(Token, result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graph_address_without_https_is_refused_unless_it_is_loopback()
    {
        // Plain HTTP would put a workspace's token on the wire in clear. Loopback
        // is allowed so local verification can run against a stub on this machine.
        WhatsAppTransportOptions insecure = new()
        {
            GraphBaseAddress = new Uri("http://graph.example.com/"),
        };

        Assert.Throws<InvalidOperationException>(insecure.Validate);

        WhatsAppTransportOptions local = new()
        {
            GraphBaseAddress = new Uri("http://127.0.0.1:5099/"),
        };

        local.Validate();
    }

    [Fact]
    public void A_malformed_graph_version_stops_the_deployment()
    {
        WhatsAppTransportOptions options = new() { GraphApiVersion = "latest" };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    /// <summary>The production client, with memory where its socket would be.</summary>
    private sealed class Transport
    {
        public required CannedHandler Handler { get; init; }

        public required HttpClient Client { get; init; }

        public required MetaWhatsAppMessageSender Sender { get; init; }

        public static Transport Answering(HttpStatusCode status, string body) =>
            Build(new CannedHandler { Status = status, Body = body });

        public static Transport Failing(Exception failure) =>
            Build(new CannedHandler { Failure = failure });

        private static Transport Build(CannedHandler handler)
        {
            WhatsAppTransportOptions options = new()
            {
                GraphBaseAddress = new Uri("https://graph.local.test/"),
            };

            options.Validate();

            ServiceCollection services = new();

            services
                .AddHttpClient(
                    WhatsAppTransportOptions.HttpClientName,
                    client =>
                    {
                        client.BaseAddress = options.GraphBaseAddress;
                        client.Timeout = options.Timeout;
                    })
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            ServiceProvider provider = services.BuildServiceProvider();

            IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

            return new Transport
            {
                Handler = handler,
                Client = factory.CreateClient(WhatsAppTransportOptions.HttpClientName),
                Sender = new MetaWhatsAppMessageSender(factory, Options.Create(options)),
            };
        }
    }

    /// <summary>
    /// A handler that answers from memory and records what it was asked.
    /// </summary>
    /// <remarks>
    /// Installed as the <em>primary</em> handler, so everything the production
    /// pipeline puts in front of it is still exercised -- which is what makes
    /// "nothing retried" a real observation rather than an assumption.
    /// </remarks>
    private sealed class CannedHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string Body { get; init; } = "{}";

        public Exception? Failure { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(request);

            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (Failure is not null)
            {
                throw Failure;
            }

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
