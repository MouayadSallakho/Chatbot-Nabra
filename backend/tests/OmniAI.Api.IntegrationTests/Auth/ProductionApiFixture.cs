using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// The API hosted as a real deployment: <c>Production</c>, behind a reverse proxy.
/// </summary>
/// <remarks>
/// <strong>This exists because the behaviour that matters cannot be observed in
/// the default test host.</strong> <c>WebApplicationFactory</c> hosts an
/// application as <c>Development</c>, which is the one environment allowed to
/// serve cookies over plain HTTP — so a suite that only ever saw Development could
/// never notice the production guarantee being weakened.
/// <para>
/// <strong>It represents the connection, not the request.</strong> The trust
/// boundary for forwarded headers is <em>where the TCP connection came from</em>,
/// which no HTTP header can express. That is simulated by setting
/// <c>Connection.RemoteIpAddress</c> through an <see cref="IStartupFilter"/>,
/// which inserts middleware ahead of everything in <c>Program.cs</c> — including
/// <c>UseForwardedHeaders</c>, the component that has to see it.
/// </para>
/// <para>
/// That is what makes the spoofing test faithful rather than decorative: the same
/// application, the same options and the same header serve both the trusted and
/// the untrusted case, and <strong>only the simulated peer address differs</strong>.
/// Nothing in the production implementation was weakened or parameterised to make
/// the test possible.
/// </para>
/// </remarks>
public abstract class ProductionApiFixture : IAsyncLifetime
{
    private const string ConnectionStringSetting = "ConnectionStrings:OmniAiDatabase";

    /// <summary>Blanked so no machine's real key can reach this host.</summary>
    private const string ApiKeySetting = "OPENAI_API_KEY";

    private readonly PostgresTestDatabase _database = new();
    private readonly ConcurrentBag<string> _failures = [];

    private WebApplicationFactory<Program>? _application;

    /// <summary>The address the simulated TCP connection appears to come from.</summary>
    protected abstract IPAddress RemoteAddress { get; }

    /// <summary>
    /// The proxy addresses this deployment is configured to trust.
    /// </summary>
    /// <remarks>
    /// Supplied as ordinary configuration, exactly as a deployment would supply
    /// it. An empty list leaves the framework's loopback defaults in place.
    /// </remarks>
    protected virtual IReadOnlyList<string> TrustedProxies => [];

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting(ConnectionStringSetting, _database.ConnectionString);
                builder.UseSetting(ApiKeySetting, string.Empty);

                for (int index = 0; index < TrustedProxies.Count; index++)
                {
                    builder.UseSetting(
                        $"ForwardedHeaders:KnownProxies:{index}",
                        TrustedProxies[index]);
                }

                builder.ConfigureServices(services =>
                {
                    // Production hides exception detail from the response, which is
                    // correct and also means a failing test here would report a
                    // bare 500. The server's own log is captured so an assertion
                    // can quote what actually went wrong.
                    services.AddSingleton<ILoggerProvider>(
                        new CapturingLoggerProvider(_failures));

                    // Sets the peer address before any application middleware sees
                    // the request. IStartupFilter is the supported way to get in
                    // front of the pipeline Program.cs builds.
                    services.AddSingleton<IStartupFilter>(
                        new RemoteAddressStartupFilter(RemoteAddress));
                });
            });
    }

    public async Task DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>
    /// A client that stands in for the reverse proxy, not for the browser.
    /// </summary>
    /// <remarks>
    /// <strong>Automatic cookie handling is off, and that is the point.</strong> A
    /// cookie jar refuses to send a <c>Secure</c> cookie back over plain HTTP —
    /// correctly, because it models a browser. This client is not a browser: it is
    /// the proxy, whose own hop to Kestrel is plain HTTP and which forwards
    /// whatever the browser sent over its HTTPS connection verbatim.
    /// <para>
    /// Letting the jar drop the cookies would have tested the test harness rather
    /// than the application. Giving the client an <c>https://</c> base address
    /// would have been worse: <c>TestServer</c> would then report the scheme as
    /// HTTPS by itself, and the forwarded header — the thing under test — would
    /// have made no difference at all.
    /// </para>
    /// </remarks>
    public HttpClient CreateClient() =>
        (_application ?? throw new InvalidOperationException("The fixture was not initialised."))
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Whatever the server logged at error level or above.</summary>
    public string ServerErrors => _failures.IsEmpty
        ? "(the server logged no errors)"
        : string.Join(" | ", _failures);

    /// <summary>Stamps every request with a simulated peer address.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            builder =>
            {
                builder.Use(async (context, proceed) =>
                {
                    context.Connection.RemoteIpAddress = address;

                    await proceed().ConfigureAwait(false);
                });

                next(builder);
            };
    }

    /// <summary>Records error-level log entries so a test can quote them.</summary>
    private sealed class CapturingLoggerProvider(ConcurrentBag<string> failures) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(failures);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentBag<string> failures) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel < LogLevel.Error)
                {
                    return;
                }

                ArgumentNullException.ThrowIfNull(formatter);

                failures.Add($"{formatter(state, exception)} :: {exception}");
            }
        }
    }
}

/// <summary>
/// Production behind a proxy this deployment trusts.
/// </summary>
/// <remarks>
/// Loopback, which is the same-host reverse proxy topology this platform expects.
/// It is named explicitly in configuration rather than left to the framework's
/// default, so the test exercises the configured path.
/// </remarks>
public sealed class TrustedProxyApiFixture : ProductionApiFixture
{
    protected override IPAddress RemoteAddress => IPAddress.Loopback;

    protected override IReadOnlyList<string> TrustedProxies => ["127.0.0.1"];
}

/// <summary>
/// Production, with the request arriving from somewhere this deployment does not
/// trust.
/// </summary>
/// <remarks>
/// The address is from <c>203.0.113.0/24</c> (TEST-NET-3, RFC 5737), reserved for
/// documentation and unroutable. It stands for any caller that reaches Kestrel
/// without passing through the proxy.
/// </remarks>
public sealed class UntrustedPeerApiFixture : ProductionApiFixture
{
    protected override IPAddress RemoteAddress => IPAddress.Parse("203.0.113.7");

    protected override IReadOnlyList<string> TrustedProxies => ["127.0.0.1"];
}
