using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// The account suites' host (Phase 37.3): a real loopback SMTP server, the real
/// account email worker on a short interval, and every log line kept.
/// </summary>
/// <remarks>
/// <para>
/// Configuration only, as an operator would write it: a loopback SMTP host with no
/// TLS and no credentials, the admin application's address, and the account
/// limits a suite needs. Nothing in the application is replaced except the log
/// sink, which only listens.
/// </para>
/// <para>
/// The log capture is how the suites prove that no token, link or password ever
/// reaches a log: every category, every level.
/// </para>
/// </remarks>
public class AccountApiFixture : RoleApiFixture, IDisposable
{
    public const string AppBase = "http://localhost:4200/";

    private readonly SmtpSink _sink = new();

    internal SmtpSink Sink => _sink;

    public CapturedLogs Logs { get; } = new();

    /// <summary>The account limits a suite runs with. Generous unless a suite is about them.</summary>
    protected virtual IReadOnlyDictionary<string, string> Limits => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AccountProtection:SignInFailuresPerAddressOnNetwork:Limit"] = "1000",
        ["AccountProtection:SignInFailuresPerNetwork:Limit"] = "100000",
        ["AccountProtection:SignInFailuresPerAddress:Limit"] = "1000",
        ["AccountProtection:ResetRequestsPerAddress:Limit"] = "1000",
        ["AccountProtection:ResetRequestsPerNetwork:Limit"] = "100000",
        ["AccountProtection:ResetsPerNetwork:Limit"] = "100000",
    };

    protected override IReadOnlyDictionary<string, string> HostSettings
    {
        get
        {
            Dictionary<string, string> settings = new(StringComparer.Ordinal)
            {
                ["Email:Smtp:Host"] = "127.0.0.1",
                ["Email:Smtp:Port"] = _sink.Port.ToString(CultureInfo.InvariantCulture),
                ["Email:Smtp:Security"] = "None",
                ["Email:Smtp:FromAddress"] = "nabra@example.test",
                ["Email:AppBaseUrl"] = AppBase,
                ["Notifications:Workers:EmailInterval"] = "00:00:00.200",
            };

            foreach ((string key, string value) in Limits)
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<ILoggerProvider>(Logs);

    /// <summary>Waits for the next message to an address, and decodes it.</summary>
    public async Task<MimeMessage> WaitForEmailAsync(string to, int skip = 0, TimeSpan? within = null)
    {
        DateTime deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(20));

        while (DateTime.UtcNow < deadline)
        {
            SinkMessage[] matching =
            [
                .. _sink.Messages.Where(message => message.Recipients.Any(recipient =>
                    recipient.Contains(to, StringComparison.OrdinalIgnoreCase))),
            ];

            if (matching.Length > skip)
            {
                return matching[skip].Parse();
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No email {skip + 1} arrived for the address.");
    }

    /// <summary>How many messages an address has been sent so far.</summary>
    public int EmailsTo(string to) =>
        _sink.Messages.Count(message => message.Recipients.Any(recipient =>
            recipient.Contains(to, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The link in an email's text part that opens a page, with its fragment.</summary>
    public static Uri LinkTo(MimeMessage email, string page)
    {
        ArgumentNullException.ThrowIfNull(email);

        Match link = Regex.Match(email.TextBody ?? string.Empty, Regex.Escape(AppBase.TrimEnd('/') + page) + @"#\S+");

        return link.Success
            ? new Uri(link.Value)
            : throw new InvalidOperationException("The email has no link to that page.");
    }

    /// <summary>One name=value pair from a link's fragment.</summary>
    public static string Fragment(Uri link, string name)
    {
        ArgumentNullException.ThrowIfNull(link);

        foreach (string pair in link.Fragment.TrimStart('#').Split('&'))
        {
            string[] parts = pair.Split('=', 2);

            if (parts.Length == 2 && parts[0] == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"The link has no {name}.");
    }

    /// <summary>Signs in a fresh client; the response is the caller's.</summary>
    public async Task<(HttpClient Client, HttpResponseMessage Response)> SignInAsync(string email, string password)
    {
        HttpClient client = CreateClient();

        return (client, await ApiAuthentication.SignInAsync(client, email, password));
    }

    /// <summary>A problem response's <c>code</c>.</summary>
    public static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    /// <summary>Asks for a reset link for an address.</summary>
    public static async Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email)
    {
        ArgumentNullException.ThrowIfNull(client);

        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        return await client.PostAsJsonAsync("/api/auth/password/forgot", new { email });
    }

    /// <summary>Uses a reset link.</summary>
    public static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string? userId, string? token, string? newPassword)
    {
        ArgumentNullException.ThrowIfNull(client);

        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        return await client.PostAsJsonAsync("/api/auth/password/reset", new { userId, token, newPassword });
    }

    public void Dispose()
    {
        _sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Every log line the host writes, rendered, for asserting what never appears.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lines.Enqueue(string.Concat(category, " ", formatter(state, exception), " ", exception?.ToString()));

            // State values too, in case a template names a value it does not print.
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (KeyValuePair<string, object?> value in values)
                {
                    lines.Enqueue(string.Concat(category, " ", value.Key, "=", Convert.ToString(value.Value, CultureInfo.InvariantCulture)));
                }
            }
        }
    }
}
