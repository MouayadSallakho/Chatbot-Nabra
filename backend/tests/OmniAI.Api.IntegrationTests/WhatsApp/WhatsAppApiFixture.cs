using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Api.IntegrationTests.WebChat;
using OmniAI.Application.WhatsApp;
using OmniAI.Infrastructure.Security;
using OmniAI.Infrastructure.DependencyInjection;

namespace OmniAI.Api.IntegrationTests.WhatsApp;

/// <summary>
/// The real API with a WhatsApp channel that reaches nothing.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No Meta account is involved anywhere in this suite.</strong> The app
/// secret, the verify token, the WABA id, the phone number id and the access
/// token are all invented here. The signature Meta would compute is computed by
/// the test, because it is an HMAC over the request body and needs no account to
/// produce.
/// </para>
/// <para>
/// <strong>The outbound sender is replaced and the Graph address points at a
/// name that does not resolve.</strong> Two independent reasons why a request can
/// never leave this machine, because one of them could be removed by accident.
/// </para>
/// </remarks>
public sealed class WhatsAppApiFixture : RoleApiFixture
{
    /// <summary>The app secret this host verifies signatures against.</summary>
    public const string AppSecret = "fake-local-app-secret-for-tests";

    /// <summary>The token this host expects Meta to echo when subscribing.</summary>
    public const string VerifyToken = "fake-local-verify-token-for-tests";

    /// <summary>An access token shaped like a credential and worth nothing.</summary>
    public const string AccessToken = "fake-local-access-token-not-a-real-credential";

    /// <summary>Where the webhook lives.</summary>
    public static Uri Webhook { get; } = WebChatApiFixture.Relative("/api/whatsapp/webhook");

    private readonly RecordingMetaStub _stub = new();

    /// <summary>Every send this host attempted. Nothing reached a network.</summary>
    internal RecordingMetaStub Stub => _stub;

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The two platform secrets, supplied the way a deployment supplies
            // them: flat configuration keys, never a tenant field and never a
            // committed settings file.
            [WhatsAppServiceCollectionExtensions.AppSecretVariable] = AppSecret,
            [WhatsAppServiceCollectionExtensions.VerifyTokenVariable] = VerifyToken,

            // A real master key, generated per host. Credentials stored by these
            // tests are genuinely encrypted and genuinely unreadable afterwards.
            [SecretProtectionSetup.MasterKeyVariable] =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),

            // A hostname that does not resolve, so even a bug that bypassed the
            // replaced sender could not reach Meta.
            ["WhatsApp:GraphBaseAddress"] = "https://graph.invalid.test/",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<IWhatsAppMessageSender>(_stub));
    }

    /// <summary>The signature Meta would send for this body.</summary>
    public static string Sign(string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(AppSecret),
            Encoding.UTF8.GetBytes(body)));

    /// <summary>Posts a body to the webhook with a signature the test chooses.</summary>
    public static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string body,
        string? signature)
    {
        ArgumentNullException.ThrowIfNull(client);

        HttpRequestMessage request = new(HttpMethod.Post, Webhook)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", signature);
        }

        return await client.SendAsync(request);
    }

    /// <summary>A connection in Draft, with no credential yet.</summary>
    public static async Task<(Guid ConnectionId, string Tag, Guid AgentId)> ConnectAsync(
        HttpClient owner,
        Guid tenantId,
        string phoneNumberId,
        string agentName = "Support")
    {
        ArgumentNullException.ThrowIfNull(owner);

        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, agentName);

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Connections(tenantId),
            new
            {
                aiAgentId = agentId,
                wabaId = "111222333444555",
                phoneNumberId,
                displayPhoneNumber = "+44 7700 900000",
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        ConnectionPayload? payload = await created.Content.ReadFromJsonAsync<ConnectionPayload>();

        Assert.NotNull(payload);

        string tag = created.Headers.ETag?.ToString() ?? throw new InvalidOperationException(
            "The create response carried no entity tag.");

        return (payload.Id, tag, agentId);
    }

    /// <summary>A connection that is serving: credential stored, status active.</summary>
    public static async Task<Guid> ServingAsync(
        HttpClient owner,
        Guid tenantId,
        string phoneNumberId,
        string agentName = "Support")
    {
        (Guid connectionId, string tag, _) =
            await ConnectAsync(owner, tenantId, phoneNumberId, agentName);

        HttpResponseMessage stored = await PutAsync(
            owner,
            Credential(tenantId, connectionId),
            new { accessToken = AccessToken },
            tag);

        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        HttpResponseMessage activated = await PostAsync(
            owner,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/whatsapp/connections/{connectionId}/activate"),
            stored.Headers.ETag?.ToString());

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        return connectionId;
    }

    /// <summary>A conditional PUT.</summary>
    public static async Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        Uri address,
        object body,
        string? tag)
    {
        ArgumentNullException.ThrowIfNull(client);

        HttpRequestMessage request = new(HttpMethod.Put, address)
        {
            Content = JsonContent.Create(body),
        };

        if (tag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        }

        return await client.SendAsync(request);
    }

    /// <summary>A conditional POST with no body.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Uri address,
        string? tag)
    {
        ArgumentNullException.ThrowIfNull(client);

        HttpRequestMessage request = new(HttpMethod.Post, address);

        if (tag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        }

        return await client.SendAsync(request);
    }

    public static Uri Connections(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/whatsapp/connections");

    public static Uri Connection(Guid tenantId, Guid connectionId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/whatsapp/connections/{connectionId}");

    public static Uri Credential(Guid tenantId, Guid connectionId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/whatsapp/connections/{connectionId}/credential");

    /// <summary>A phone number id no other test in this run will claim.</summary>
    public static string Number() =>
        Random.Shared.NextInt64(100_000_000_000_000, 999_999_999_999_999)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>One connection, as the API renders it.</summary>
    public sealed record ConnectionPayload(
        Guid Id,
        Guid AiAgentId,
        string? AiAgentName,
        string WabaId,
        string PhoneNumberId,
        string? DisplayPhoneNumber,
        string Status,
        bool HasCredential);
}

/// <summary>
/// The local Meta stub. It records and answers; it never opens a socket.
/// </summary>
internal sealed class RecordingMetaStub : IWhatsAppMessageSender
{
    private readonly Lock _sync = new();

    private readonly List<Send> _sends = [];

    /// <summary>Every send attempted, in order.</summary>
    public IReadOnlyList<Send> Sends
    {
        get
        {
            lock (_sync)
            {
                return [.. _sends];
            }
        }
    }

    /// <summary>Forgets what earlier tests did.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _sends.Clear();
        }
    }

    public Task<WhatsAppSendResult> SendTextAsync(
        OmniAI.Domain.WhatsApp.PhoneNumberId sendFrom,
        OmniAI.Domain.WhatsApp.WhatsAppCustomerId recipient,
        string text,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        string id = $"wamid.STUB{Guid.NewGuid():N}==";

        lock (_sync)
        {
            _sends.Add(new Send(sendFrom.Value, recipient.Value, text, accessToken, id));
        }

        return Task.FromResult(WhatsAppSendResult.Accepted(
            OmniAI.Domain.WhatsApp.ProviderMessageId.Create(id)));
    }

    /// <summary>One attempted send, with everything the platform handed the transport.</summary>
    internal sealed record Send(
        string From,
        string To,
        string Text,
        string AccessToken,
        string ProviderMessageId);
}
