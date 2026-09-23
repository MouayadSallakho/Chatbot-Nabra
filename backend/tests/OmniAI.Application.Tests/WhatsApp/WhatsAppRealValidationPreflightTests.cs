using OmniAI.Application.Security;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.Tests.WhatsApp;

/// <summary>
/// What the platform can say about its own readiness before anybody contacts the
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here contacts anybody.</strong> The preflight holds no HTTP
/// client and is given none; every answer is read from configuration, from the
/// stored connection and from a secret round trip on this machine.
/// </para>
/// <para>
/// <strong>The detail sentences are asserted to disclose nothing.</strong> A
/// readiness report is exactly the kind of output somebody pastes into a chat
/// while asking for help, so the test that it names no secret matters as much as
/// the tests that it counts correctly.
/// </para>
/// </remarks>
public sealed class WhatsAppRealValidationPreflightTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private const string ConnectedNumber = "100200300400500";

    [Fact]
    public async Task A_configured_deployment_with_a_serving_connection_is_ready()
    {
        Scenario scenario = Scenario.Build();

        WhatsAppReadinessReport report = await scenario.InspectAsync();

        Assert.True(report.IsReady);
        Assert.Empty(report.Blockers);

        // Every check reported, not just the failing ones: an operator reading a
        // short report cannot tell whether an item passed or was skipped.
        Assert.Equal(10, report.Items.Count);
    }

    [Fact]
    public async Task Missing_webhook_secrets_are_a_blocker()
    {
        // The deployment could send but could never receive, which is half a
        // validation and the more confusing half.
        Scenario scenario = Scenario.Build(webhookConfigured: false);

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.WebhookAuthenticationCheck));
    }

    [Fact]
    public async Task Pointing_at_a_real_provider_with_sending_switched_off_is_a_blocker()
    {
        // The Development guard doing its job. It is correct behaviour and still
        // a blocker: a validation session that begins in this state would see
        // every send refused in-process.
        Scenario scenario = Scenario.Build(live: true, liveSendingAllowed: false);

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.LiveSendingCheck));
    }

    [Fact]
    public async Task A_local_stub_is_ready_rather_than_blocked()
    {
        // Withholding live sending is the correct state against a stub, and
        // reporting it as a blocker would train an operator to ignore the one
        // item that matters.
        Scenario scenario = Scenario.Build(live: false, liveSendingAllowed: false);

        WhatsAppReadinessReport report = await scenario.InspectAsync();

        Assert.Equal(
            WhatsAppReadinessState.Ready,
            State(report, WhatsAppRealValidationPreflight.LiveSendingCheck));

        Assert.True(report.IsReady);
    }

    [Fact]
    public async Task An_address_a_credential_may_not_be_sent_to_is_invalid()
    {
        Scenario scenario = Scenario.Build(addressUsable: false);

        Assert.Equal(
            WhatsAppReadinessState.Invalid,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.ProviderAddressCheck));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_provider_version_is_invalid(string version)
    {
        Scenario scenario = Scenario.Build(apiVersion: version, versionUsable: false);

        Assert.Equal(
            WhatsAppReadinessState.Invalid,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.ProviderVersionCheck));
    }

    [Fact]
    public async Task A_deployment_that_cannot_protect_secrets_is_a_blocker()
    {
        Scenario scenario = Scenario.Build(secrets: SecretBehaviour.Unavailable);

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.SecretProtectionCheck));
    }

    [Fact]
    public async Task An_unknown_connection_still_reports_the_deployments_own_checks()
    {
        // The operator asking about the wrong id should still learn whether the
        // deployment itself is ready, rather than getting one line back.
        Scenario scenario = Scenario.Build();

        WhatsAppReadinessReport report = await scenario.InspectAsync(WhatsAppConnectionId.New());

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            State(report, WhatsAppRealValidationPreflight.ConnectionCheck));

        Assert.Equal(
            WhatsAppReadinessState.Ready,
            State(report, WhatsAppRealValidationPreflight.WebhookAuthenticationCheck));
    }

    [Fact]
    public async Task A_connection_in_another_workspace_reads_as_absent()
    {
        // Indistinguishable from "does not exist", as everywhere else: a
        // preflight is not a place to learn which connection ids a deployment
        // holds.
        Scenario scenario = Scenario.Build();

        WhatsAppReadinessReport report = await scenario.InspectAsync(
            tenantId: TenantId.New(),
            connectionId: scenario.ConnectionId);

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            State(report, WhatsAppRealValidationPreflight.ConnectionCheck));
    }

    [Fact]
    public async Task A_connection_with_no_credential_and_no_activation_reports_both()
    {
        Scenario scenario = Scenario.Build(serving: false);

        WhatsAppReadinessReport report = await scenario.InspectAsync();

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            State(report, WhatsAppRealValidationPreflight.ConnectionCredentialCheck));

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            State(report, WhatsAppRealValidationPreflight.ConnectionActiveCheck));

        // And the identifiers are still there, so the operator can see what they
        // have rather than only what they lack.
        Assert.Equal(
            WhatsAppReadinessState.Ready,
            State(report, WhatsAppRealValidationPreflight.ConnectionIdentifiersCheck));
    }

    [Fact]
    public async Task A_credential_protected_under_a_different_key_is_invalid_rather_than_missing()
    {
        // The failure a rotated master key produces. The connection still says it
        // has a credential, and without this the first send of a validation
        // session is what discovers otherwise.
        Scenario scenario = Scenario.Build(secrets: SecretBehaviour.WrongKey);

        Assert.Equal(
            WhatsAppReadinessState.Invalid,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.ConnectionCredentialCheck));
    }

    [Fact]
    public async Task An_agent_that_does_not_exist_in_the_workspace_is_a_blocker()
    {
        Scenario scenario = Scenario.Build(agentExists: false);

        Assert.Equal(
            WhatsAppReadinessState.Missing,
            await scenario.StateOfAsync(WhatsAppRealValidationPreflight.ConnectionAgentCheck));
    }

    [Fact]
    public async Task No_detail_sentence_carries_a_secret_or_an_address()
    {
        // The whole report, in the state most likely to be pasted somewhere.
        Scenario scenario = Scenario.Build(live: true, liveSendingAllowed: false);

        WhatsAppReadinessReport report = await scenario.InspectAsync();

        string everything = string.Join(
            '\n',
            report.Items.Select(item => $"{item.Check} {item.State} {item.Detail}"));

        foreach (string forbidden in (string[])
        [
            Scenario.AccessToken,
            Scenario.AppSecret,
            Scenario.VerifyToken,
            "graph.facebook.com",
            "https://",
        ])
        {
            Assert.DoesNotContain(forbidden, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Ready_never_means_the_provider_accepted_anything()
    {
        // A local rehearsal reports ready. It has to be impossible to read that
        // as "connected", which is why nothing in this report says so and why the
        // local case says outright what it is.
        Scenario scenario = Scenario.Build(live: false, liveSendingAllowed: false);

        WhatsAppReadinessReport report = await scenario.InspectAsync();

        Assert.True(report.IsReady);

        string everything = string.Join('\n', report.Items.Select(item => item.Detail));

        Assert.DoesNotContain("verified", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connected to", everything, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "local",
            State(report, WhatsAppRealValidationPreflight.LiveSendingCheck) == WhatsAppReadinessState.Ready
                ? Detail(report, WhatsAppRealValidationPreflight.LiveSendingCheck)
                : string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------------- helpers

    private static WhatsAppReadinessState State(WhatsAppReadinessReport report, string check) =>
        report.Items.Single(item => item.Check == check).State;

    private static string Detail(WhatsAppReadinessReport report, string check) =>
        report.Items.Single(item => item.Check == check).Detail;

    /// <summary>How the deployment's secret protection behaves.</summary>
    private enum SecretBehaviour
    {
        /// <summary>A key is configured and works.</summary>
        Working = 0,

        /// <summary>No key at all.</summary>
        Unavailable = 1,

        /// <summary>
        /// A key that works for new values but cannot read what is already
        /// stored: a rotation nobody re-entered credentials after.
        /// </summary>
        WrongKey = 2,
    }

    /// <summary>One deployment, one workspace, one connection.</summary>
    private sealed class Scenario
    {
        public const string AccessToken = "fake-local-test-token";

        public const string AppSecret = "fake-local-app-secret";

        public const string VerifyToken = "fake-local-verify-token";

        public required WhatsAppRealValidationPreflight Preflight { private get; init; }

        public required TenantId Workspace { get; init; }

        public required WhatsAppConnectionId ConnectionId { get; init; }

        public static Scenario Build(
            bool webhookConfigured = true,
            bool live = true,
            bool liveSendingAllowed = true,
            bool addressUsable = true,
            bool versionUsable = true,
            string apiVersion = "v26.0",
            bool serving = true,
            bool agentExists = true,
            SecretBehaviour secrets = SecretBehaviour.Working)
        {
            TenantId workspace = TenantId.New();
            AiAgentId agent = AiAgentId.New();

            WhatsAppWorld world = new();

            WhatsAppConnection connection = serving
                ? world.Serving(workspace, agent, ConnectedNumber, Noon)
                : world.Draft(workspace, agent, ConnectedNumber, Noon);

            if (agentExists)
            {
                world.AgentNames[agent] = "Support";
            }

            return new Scenario
            {
                Workspace = workspace,
                ConnectionId = connection.Id,
                Preflight = new WhatsAppRealValidationPreflight(
                    new StubWebhookAuthenticator(webhookConfigured),
                    new StubProviderReadiness(new WhatsAppProviderReadiness(
                        addressUsable,
                        apiVersion,
                        versionUsable,
                        live,
                        liveSendingAllowed)),
                    new PreflightSecretProtector(secrets),
                    world),
            };
        }

        public Task<WhatsAppReadinessReport> InspectAsync(WhatsAppConnectionId? connectionId = null) =>
            Preflight.InspectAsync(Workspace, connectionId ?? ConnectionId);

        public Task<WhatsAppReadinessReport> InspectAsync(
            TenantId tenantId,
            WhatsAppConnectionId connectionId) =>
            Preflight.InspectAsync(tenantId, connectionId);

        public async Task<WhatsAppReadinessState> StateOfAsync(string check) =>
            State(await InspectAsync(), check);
    }

    private sealed class StubWebhookAuthenticator(bool configured) : IWhatsAppWebhookAuthenticator
    {
        public bool IsConfigured => configured;

        public bool IsVerifyTokenValid(string? presented) => configured;

        public bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? header) => configured;
    }

    private sealed class StubProviderReadiness(WhatsAppProviderReadiness facts)
        : IWhatsAppProviderReadiness
    {
        public WhatsAppProviderReadiness Describe() => facts;
    }

    /// <summary>
    /// A protector with the three behaviours a preflight has to tell apart.
    /// </summary>
    /// <remarks>
    /// <see cref="SecretBehaviour.WrongKey"/> is the interesting one: it protects
    /// new values perfectly well and cannot read the one already in the database,
    /// which is what a rotated master key looks like from here.
    /// </remarks>
    private sealed class PreflightSecretProtector(SecretBehaviour behaviour) : ISecretProtector
    {
        /// <summary>Ciphertext this instance recognises as its own.</summary>
        private readonly Dictionary<string, string> _mine = [];

        public byte[] Protect(string secret, SecretContext context)
        {
            if (behaviour == SecretBehaviour.Unavailable)
            {
                throw new SecretProtectionUnavailableException(
                    "No secret protection key is configured.");
            }

            string handle = Guid.CreateVersion7().ToString("N");

            _mine[handle] = secret;

            return System.Text.Encoding.UTF8.GetBytes(handle);
        }

        public string? Unprotect(byte[]? protectedSecret, SecretContext context)
        {
            if (behaviour == SecretBehaviour.Unavailable || protectedSecret is null)
            {
                return null;
            }

            string handle = System.Text.Encoding.UTF8.GetString(protectedSecret);

            // The stored credential was protected elsewhere, so it is not in this
            // instance's map. Under WrongKey that is the whole point; under
            // Working it stands in for the real ciphertext.
            return _mine.TryGetValue(handle, out string? secret)
                ? secret
                : behaviour == SecretBehaviour.Working ? Scenario.AccessToken : null;
        }
    }
}
