using OmniAI.Application.Concurrency;
using OmniAI.Application.Security;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>How one readiness item stands.</summary>
public enum WhatsAppReadinessState
{
    /// <summary>Nothing more to do for this item.</summary>
    Ready = 1,

    /// <summary>Something has not been supplied yet.</summary>
    Missing = 2,

    /// <summary>Something was supplied and cannot be used as it stands.</summary>
    Invalid = 3,
}

/// <summary>One thing that must hold before a real provider validation begins.</summary>
/// <param name="Check">
/// A stable identifier, so a runbook can refer to an item by name and a test can
/// assert on one without matching prose.
/// </param>
/// <param name="State">How that item stands.</param>
/// <param name="Detail">
/// A sentence for the person running the validation.
/// <strong>Never a value.</strong> It may say that a credential is configured and
/// recoverable; it may never say what it is, what it starts with or how long it
/// is. The same applies to the app secret, the verify token and the master key —
/// a readiness answer that discloses its inputs is a credential dump with a
/// friendly name.
/// </param>
public sealed record WhatsAppReadinessItem(
    string Check,
    WhatsAppReadinessState State,
    string Detail);

/// <summary>Everything the preflight looked at.</summary>
/// <param name="Items">Each check, in the order it was made.</param>
public sealed record WhatsAppReadinessReport(IReadOnlyList<WhatsAppReadinessItem> Items)
{
    /// <summary>Whether a real validation could begin.</summary>
    public bool IsReady => Items.All(item => item.State == WhatsAppReadinessState.Ready);

    /// <summary>The items that are not ready, in the order they were checked.</summary>
    public IReadOnlyList<WhatsAppReadinessItem> Blockers =>
        [.. Items.Where(item => item.State != WhatsAppReadinessState.Ready)];
}

/// <summary>The transport facts the preflight reasons about.</summary>
/// <param name="AddressIsUsable">
/// The configured provider address is one this platform will send a credential
/// to: HTTPS, or loopback where there is no wire.
/// </param>
/// <param name="ApiVersion">
/// The pinned provider API version. Not a secret — it appears in every request
/// path and in the provider's own documentation.
/// </param>
/// <param name="VersionIsUsable">The pinned version is present and well formed.</param>
/// <param name="TargetsLiveProvider">
/// The deployment is aimed at something off this machine.
/// </param>
/// <param name="LiveSendingAllowed">
/// Sending would actually leave the process. False when the Development guard is
/// withholding it.
/// </param>
public sealed record WhatsAppProviderReadiness(
    bool AddressIsUsable,
    string ApiVersion,
    bool VersionIsUsable,
    bool TargetsLiveProvider,
    bool LiveSendingAllowed);

/// <summary>
/// What the transport side of the deployment can say about itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Facts, not configuration.</strong> No base address, no app secret, no
/// credential — only whether the address is one a credential may be sent to,
/// whether the pinned version is usable, whether this deployment is aimed at a
/// real provider at all, and whether it is permitted to send. The Application
/// still knows nothing about Graph; it knows whether the thing underneath it
/// could work.
/// </para>
/// <para>
/// It exists because the preflight's whole purpose is to answer a question about
/// Infrastructure from a place that can be tested without one.
/// </para>
/// </remarks>
public interface IWhatsAppProviderReadiness
{
    /// <summary>Describes this deployment's outbound configuration.</summary>
    WhatsAppProviderReadiness Describe();
}

/// <summary>
/// Answers, without contacting the provider, whether a real validation could
/// begin.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It exists so that a validation session fails on this machine rather
/// than in front of the provider.</strong> Real validation is a scheduled thing
/// somebody sets aside time for, often with a tunnel open and a real phone in
/// hand; discovering then that the master key was rotated, or that the webhook
/// secrets were never set, costs the whole session. Every check here is one that
/// has a boring local answer and an expensive remote one.
/// </para>
/// <para>
/// <strong>It contacts nobody.</strong> No request is made to the provider, and
/// none could be: this type holds no HTTP client. "Ready" therefore means this
/// deployment has done its part — it never means the provider has accepted
/// anything, and nothing in the product may present it as verification.
/// </para>
/// <para>
/// <strong>It has no HTTP surface, deliberately.</strong> It reports on the
/// deployment's secrets and on a workspace's credential by recoverability, which
/// is exactly the shape of thing that becomes an information leak the moment it
/// is reachable. It is composed and exercised from tests and from an operator
/// harness; if it is ever exposed, that needs its own authorization decision.
/// </para>
/// </remarks>
public sealed class WhatsAppRealValidationPreflight(
    IWhatsAppWebhookAuthenticator authenticator,
    IWhatsAppProviderReadiness provider,
    ISecretProtector secrets,
    IWhatsAppConnectionRepository connections)
{
    /// <summary>The webhook can tell a real delivery from an invented one.</summary>
    public const string WebhookAuthenticationCheck = "webhook.authentication";

    /// <summary>The provider address is one a credential may be sent to.</summary>
    public const string ProviderAddressCheck = "provider.address";

    /// <summary>The provider API version is pinned and well formed.</summary>
    public const string ProviderVersionCheck = "provider.version";

    /// <summary>Sending is permitted, not withheld by the Development guard.</summary>
    public const string LiveSendingCheck = "provider.live-sending";

    /// <summary>Stored credentials can be protected and recovered.</summary>
    public const string SecretProtectionCheck = "secrets.protection";

    /// <summary>The connection exists in this workspace.</summary>
    public const string ConnectionCheck = "connection.exists";

    /// <summary>The connection carries the provider's account identifiers.</summary>
    public const string ConnectionIdentifiersCheck = "connection.identifiers";

    /// <summary>The connection's access token is stored and recoverable.</summary>
    public const string ConnectionCredentialCheck = "connection.credential";

    /// <summary>The connection is switched on.</summary>
    public const string ConnectionActiveCheck = "connection.active";

    /// <summary>The agent it answers with exists, in the same workspace.</summary>
    public const string ConnectionAgentCheck = "connection.agent";

    /// <summary>The value the secret-protection round trip uses.</summary>
    /// <remarks>
    /// Fixed and meaningless. It is never stored, never returned and could not be
    /// mistaken for a credential if it were.
    /// </remarks>
    private const string Probe = "omniai-preflight-probe";

    /// <summary>
    /// Inspects one workspace's connection and this deployment's configuration.
    /// </summary>
    /// <remarks>
    /// Every check runs. Stopping at the first failure would mean an operator
    /// fixes one thing, runs it again and finds the next, which is how a
    /// five-minute preflight becomes an afternoon.
    /// </remarks>
    public async Task<WhatsAppReadinessReport> InspectAsync(
        TenantId tenantId,
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default)
    {
        List<WhatsAppReadinessItem> items =
        [
            InspectWebhook(),
            .. InspectProvider(),
            InspectSecretProtection(tenantId, connectionId),
        ];

        Versioned<WhatsAppConnection>? found = await connections
            .FindAsync(tenantId, connectionId, cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            // Absent and belonging-to-another-workspace are the same answer here,
            // as they are everywhere else: a preflight is not a place to learn
            // which connection ids exist on a deployment.
            items.Add(new WhatsAppReadinessItem(
                ConnectionCheck,
                WhatsAppReadinessState.Missing,
                "No WhatsApp connection with that id exists in this workspace."));

            return new WhatsAppReadinessReport(items);
        }

        WhatsAppConnection connection = found.Value;

        items.Add(new WhatsAppReadinessItem(
            ConnectionCheck,
            WhatsAppReadinessState.Ready,
            "The connection exists in this workspace."));

        items.Add(InspectIdentifiers(connection));

        items.Add(await InspectCredentialAsync(connection, cancellationToken).ConfigureAwait(false));

        items.Add(InspectStatus(connection));

        items.Add(await InspectAgentAsync(connection, cancellationToken).ConfigureAwait(false));

        return new WhatsAppReadinessReport(items);
    }

    private WhatsAppReadinessItem InspectWebhook() =>
        authenticator.IsConfigured
            ? new WhatsAppReadinessItem(
                WebhookAuthenticationCheck,
                WhatsAppReadinessState.Ready,
                "The app secret and verify token are configured, so signed deliveries can be "
                + "told from invented ones.")
            : new WhatsAppReadinessItem(
                WebhookAuthenticationCheck,
                WhatsAppReadinessState.Missing,
                "The webhook app secret and verify token are not configured, so every webhook "
                + "request is refused and no message can arrive.");

    private IEnumerable<WhatsAppReadinessItem> InspectProvider()
    {
        WhatsAppProviderReadiness facts = provider.Describe();

        yield return facts.AddressIsUsable
            ? new WhatsAppReadinessItem(
                ProviderAddressCheck,
                WhatsAppReadinessState.Ready,
                facts.TargetsLiveProvider
                    ? "The provider address is a secure remote address."
                    : "The provider address is on this machine, so this is a local rehearsal "
                        + "rather than a real validation.")
            : new WhatsAppReadinessItem(
                ProviderAddressCheck,
                WhatsAppReadinessState.Invalid,
                "The provider address would put a workspace's access token on the wire in "
                + "clear.");

        yield return facts.VersionIsUsable
            ? new WhatsAppReadinessItem(
                ProviderVersionCheck,
                WhatsAppReadinessState.Ready,
                $"Requests are pinned to provider API version {facts.ApiVersion}.")
            : new WhatsAppReadinessItem(
                ProviderVersionCheck,
                WhatsAppReadinessState.Invalid,
                "The pinned provider API version is blank or malformed.");

        // Not a failure when the deployment is aimed at a local stub: withholding
        // live sending is the correct state there, and reporting it as a blocker
        // would train an operator to ignore the one item that matters.
        yield return facts.LiveSendingAllowed || !facts.TargetsLiveProvider
            ? new WhatsAppReadinessItem(
                LiveSendingCheck,
                WhatsAppReadinessState.Ready,
                facts.TargetsLiveProvider
                    ? "Sending to the real provider is permitted on this deployment."
                    : "Sending goes to a local stub. Nothing can reach the real provider.")
            : new WhatsAppReadinessItem(
                LiveSendingCheck,
                WhatsAppReadinessState.Missing,
                "This deployment points at a real provider but live sending is switched off, "
                + "so every send would be refused in-process.");
    }

    /// <summary>
    /// Proves the deployment can protect and recover a secret, using one it
    /// invents.
    /// </summary>
    /// <remarks>
    /// A round trip rather than an "is a key configured" question, because the
    /// failure worth catching is a key that is present and wrong.
    /// </remarks>
    private WhatsAppReadinessItem InspectSecretProtection(
        TenantId tenantId,
        WhatsAppConnectionId connectionId)
    {
        SecretContext context = new(
            SecretPurposes.WhatsAppAccessToken,
            tenantId.Value,
            connectionId.Value);

        try
        {
            string? recovered = secrets.Unprotect(secrets.Protect(Probe, context), context);

            return string.Equals(recovered, Probe, StringComparison.Ordinal)
                ? new WhatsAppReadinessItem(
                    SecretProtectionCheck,
                    WhatsAppReadinessState.Ready,
                    "Secrets can be protected and recovered on this deployment.")
                : new WhatsAppReadinessItem(
                    SecretProtectionCheck,
                    WhatsAppReadinessState.Invalid,
                    "Secret protection is configured but a value protected here could not be "
                    + "recovered here.");
        }
        catch (SecretProtectionUnavailableException)
        {
            return new WhatsAppReadinessItem(
                SecretProtectionCheck,
                WhatsAppReadinessState.Missing,
                "No secret protection key is configured, so no access token can be stored.");
        }
    }

    /// <summary>Both provider identifiers are present.</summary>
    /// <remarks>
    /// They are required to create a connection, so this cannot fail today. It is
    /// checked anyway: the preflight's job is to state what is true, and an item
    /// that is silently absent is one nobody misses when a later phase makes it
    /// optional.
    /// </remarks>
    private static WhatsAppReadinessItem InspectIdentifiers(WhatsAppConnection connection) =>
        string.IsNullOrWhiteSpace(connection.WabaId.Value)
        || string.IsNullOrWhiteSpace(connection.PhoneNumberId.Value)
            ? new WhatsAppReadinessItem(
                ConnectionIdentifiersCheck,
                WhatsAppReadinessState.Missing,
                "The connection is missing the provider account or phone number identifier.")
            : new WhatsAppReadinessItem(
                ConnectionIdentifiersCheck,
                WhatsAppReadinessState.Ready,
                "The connection carries both provider identifiers.");

    private async Task<WhatsAppReadinessItem> InspectCredentialAsync(
        WhatsAppConnection connection,
        CancellationToken cancellationToken)
    {
        if (!connection.HasCredential)
        {
            return new WhatsAppReadinessItem(
                ConnectionCredentialCheck,
                WhatsAppReadinessState.Missing,
                "No access token is stored for this connection, so nothing can be sent.");
        }

        byte[]? stored = await connections
            .FindProtectedCredentialAsync(connection.Id, cancellationToken)
            .ConfigureAwait(false);

        // Recovered and immediately dropped. This is the failure a rotated master
        // key produces: the connection still says it has a credential, and
        // without this the first send of the validation session is what discovers
        // otherwise.
        string? recovered = secrets.Unprotect(
            stored,
            new SecretContext(
                SecretPurposes.WhatsAppAccessToken,
                connection.TenantId.Value,
                connection.Id.Value));

        return string.IsNullOrWhiteSpace(recovered)
            ? new WhatsAppReadinessItem(
                ConnectionCredentialCheck,
                WhatsAppReadinessState.Invalid,
                "The stored access token cannot be recovered on this deployment. It was "
                + "protected under a different key and must be entered again.")
            : new WhatsAppReadinessItem(
                ConnectionCredentialCheck,
                WhatsAppReadinessState.Ready,
                "An access token is stored and can be recovered.");
    }

    private static WhatsAppReadinessItem InspectStatus(WhatsAppConnection connection) =>
        connection.IsServing
            ? new WhatsAppReadinessItem(
                ConnectionActiveCheck,
                WhatsAppReadinessState.Ready,
                "The connection is active and serving.")
            : new WhatsAppReadinessItem(
                ConnectionActiveCheck,
                WhatsAppReadinessState.Missing,
                "The connection is not serving, so inbound messages are acknowledged and "
                + "dropped.");

    private async Task<WhatsAppReadinessItem> InspectAgentAsync(
        WhatsAppConnection connection,
        CancellationToken cancellationToken)
    {
        // Scoped to the connection's own workspace, so an agent belonging to
        // another one reads as absent rather than as a usable answer.
        string? name = await connections
            .FindAgentNameAsync(connection.TenantId, connection.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(name)
            ? new WhatsAppReadinessItem(
                ConnectionAgentCheck,
                WhatsAppReadinessState.Missing,
                "The agent this connection answers with does not exist in this workspace.")
            : new WhatsAppReadinessItem(
                ConnectionAgentCheck,
                WhatsAppReadinessState.Ready,
                "The agent this connection answers with exists in this workspace.");
    }
}
