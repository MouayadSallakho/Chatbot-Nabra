using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Security;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>Every connection in a workspace.</summary>
public sealed record ListWhatsAppConnectionsQuery(Guid TenantId);

/// <summary>One connection.</summary>
public sealed record GetWhatsAppConnectionQuery(Guid TenantId, Guid ConnectionId);

/// <summary>Connect a number to an agent. It is created switched off.</summary>
public sealed record CreateWhatsAppConnectionCommand(
    Guid TenantId,
    Guid AiAgentId,
    string? WabaId,
    string? PhoneNumberId,
    string? DisplayPhoneNumber);

/// <summary>Change which number and agent a connection uses.</summary>
public sealed record UpdateWhatsAppConnectionCommand(
    Guid TenantId,
    Guid ConnectionId,
    Guid AiAgentId,
    string? WabaId,
    string? PhoneNumberId,
    string? DisplayPhoneNumber,
    ConcurrencyToken Expected);

/// <summary>Store a new access token. Write-only.</summary>
public sealed record ReplaceWhatsAppCredentialCommand(
    Guid TenantId,
    Guid ConnectionId,
    string? AccessToken,
    ConcurrencyToken Expected);

/// <summary>Put a connection in or out of service.</summary>
public sealed record SetWhatsAppConnectionStatusCommand(
    Guid TenantId,
    Guid ConnectionId,
    bool Activate,
    ConcurrencyToken Expected);

/// <summary>
/// One connection, as every admin response renders it.
/// </summary>
/// <remarks>
/// <strong>There is no access-token field here, and no way to add one by
/// accident.</strong> The aggregate this is built from does not hold the token —
/// it holds <see cref="HasCredential"/>, a boolean — so a future field could not
/// carry a credential even if somebody tried.
/// <para>
/// Carries the agent's name because a list of numbers beside opaque agent ids is
/// unreadable, and the name is nullable because an agent can be gone: a
/// connection whose agent was removed must still appear on the one screen that
/// could repair it.
/// </para>
/// </remarks>
public sealed record WhatsAppConnectionView(
    Guid Id,
    Guid AiAgentId,
    string? AiAgentName,
    string WabaId,
    string PhoneNumberId,
    string? DisplayPhoneNumber,
    WhatsAppConnectionStatus Status,
    bool HasCredential,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Administering a workspace's WhatsApp connections.
/// </summary>
/// <remarks>
/// <strong>No read path here returns an access token, and none could.</strong>
/// The aggregate does not hold one — it holds a boolean saying whether one
/// exists — and the only method that touches the protected value is the one
/// that writes it. That is a property of the shape rather than a rule somebody
/// has to remember on every new endpoint.
/// </remarks>
public sealed class WhatsAppConnectionHandlers(
    IWorkspaceEntitlementService entitlements,
    TenantAuthorizer authorizer,
    IWhatsAppConnectionRepository connections,
    IAiAgentRepository agents,
    ISecretProtector secrets,
    TimeProvider clock)
{
    /// <summary>
    /// The longest access token this platform will store.
    /// </summary>
    /// <remarks>
    /// Meta's long-lived tokens are a few hundred characters. Four thousand is
    /// generous enough to outlast a format change and short enough that a pasted
    /// file is refused rather than encrypted.
    /// </remarks>
    public const int MaxAccessTokenLength = 4096;

    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IWhatsAppConnectionRepository _connections =
        connections ?? throw new ArgumentNullException(nameof(connections));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly ISecretProtector _secrets =
        secrets ?? throw new ArgumentNullException(nameof(secrets));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The connections, or <see langword="null"/> when the caller is not
    /// entitled.
    /// </returns>
    public async Task<IReadOnlyList<WhatsAppConnectionRow>?> ListAsync(
        ListWhatsAppConnectionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        return await ViewAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ? await _connections.ListAsync(tenantId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <returns>The connection, or <see langword="null"/>.</returns>
    public async Task<Versioned<WhatsAppConnectionView>?> GetAsync(
        GetWhatsAppConnectionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await ViewAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Versioned<WhatsAppConnection>? found = await _connections
            .FindAsync(tenantId, WhatsAppConnectionId.From(query.ConnectionId), cancellationToken)
            .ConfigureAwait(false);

        return found is null
            ? null
            : await PublishAsync(tenantId, found, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The new connection, or <see langword="null"/>.</returns>
    /// <exception cref="WhatsAppPhoneNumberInUseException">
    /// That number is already connected somewhere on this deployment.
    /// </exception>
    public async Task<Versioned<WhatsAppConnectionView>?> CreateAsync(
        CreateWhatsAppConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        if (!await ManageAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        AiAgentId agentId = await RequireAgentAsync(
                tenantId,
                command.AiAgentId,
                cancellationToken)
            .ConfigureAwait(false);

        WhatsAppConnection connection = WhatsAppConnection.Create(
            tenantId,
            agentId,
            WabaId.Create(command.WabaId),
            PhoneNumberId.Create(command.PhoneNumberId),
            command.DisplayPhoneNumber,
            _clock.GetUtcNow());


        // Permission said this person may create one; the plan says whether the
        // workspace may hold another. Two questions, asked in that order, and
        // neither answers the other. The count and the insert happen together
        // under a workspace-scoped database lock, so two requests arriving at the
        // last free slot cannot both pass the count.
        //
        // Every connection counts, whatever its status. A disabled connection is
        // still a configured channel, so turning one off does not free a slot.
        Versioned<WhatsAppConnection> stored = await _entitlements.ReserveAsync(
            tenantId,
            EntitlementKind.WhatsAppConnections,
            async token =>
            {
                ConcurrencyToken version = await _connections
                    .AddAsync(connection, token)
                    .ConfigureAwait(false);

                return new Versioned<WhatsAppConnection>(connection, version);
            },
            cancellationToken)
            .ConfigureAwait(false);

        return await PublishAsync(tenantId, stored, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The changed connection, or <see langword="null"/>.</returns>
    public async Task<Versioned<WhatsAppConnectionView>?> UpdateAsync(
        UpdateWhatsAppConnectionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        Versioned<WhatsAppConnection>? found =
            await LoadForWriteAsync(tenantId, command.ConnectionId, command.Expected, cancellationToken)
                .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        AiAgentId agentId = await RequireAgentAsync(tenantId, command.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        found.Value.Reconfigure(
            agentId,
            WabaId.Create(command.WabaId),
            PhoneNumberId.Create(command.PhoneNumberId),
            command.DisplayPhoneNumber,
            _clock.GetUtcNow());

        ConcurrencyToken version = await _connections
            .UpdateAsync(found.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return await PublishAsync(
                tenantId,
                new Versioned<WhatsAppConnection>(found.Value, version),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a new access token, protected.
    /// </summary>
    /// <remarks>
    /// <strong>The only place in the platform that holds a tenant's WhatsApp
    /// token in memory on a write path.</strong> It arrives, is protected, and
    /// is stored; nothing returns it, and the response says only that a
    /// credential is now configured.
    /// </remarks>
    /// <returns>The connection, or <see langword="null"/>.</returns>
    /// <exception cref="SecretProtectionUnavailableException">
    /// This deployment has no master key configured, so no credential can be
    /// stored safely.
    /// </exception>
    public async Task<Versioned<WhatsAppConnectionView>?> ReplaceCredentialAsync(
        ReplaceWhatsAppCredentialCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        string token = (command.AccessToken ?? string.Empty).Trim();

        if (token.Length == 0)
        {
            throw new DomainValidationException("An access token must not be blank.");
        }

        if (token.Length > MaxAccessTokenLength)
        {
            throw new DomainValidationException(
                $"An access token must be {MaxAccessTokenLength} characters or fewer.");
        }

        Versioned<WhatsAppConnection>? found =
            await LoadForWriteAsync(tenantId, command.ConnectionId, command.Expected, cancellationToken)
                .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        // Bound to this workspace, this connection and this purpose, so a
        // ciphertext lifted into another row decrypts to nothing.
        byte[] protectedToken = _secrets.Protect(
            token,
            new SecretContext(
                SecretPurposes.WhatsAppAccessToken,
                tenantId.Value,
                found.Value.Id.Value));

        found.Value.RecordCredentialStored(_clock.GetUtcNow());

        ConcurrencyToken version = await _connections
            .ReplaceCredentialAsync(found.Value, protectedToken, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return await PublishAsync(
                tenantId,
                new Versioned<WhatsAppConnection>(found.Value, version),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns>The connection, or <see langword="null"/>.</returns>
    /// <exception cref="DomainStateException">
    /// Activating without a stored access token.
    /// </exception>
    public async Task<Versioned<WhatsAppConnectionView>?> SetStatusAsync(
        SetWhatsAppConnectionStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        Versioned<WhatsAppConnection>? found =
            await LoadForWriteAsync(tenantId, command.ConnectionId, command.Expected, cancellationToken)
                .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        if (command.Activate)
        {
            found.Value.Activate(now);
        }
        else
        {
            found.Value.Disable(now);
        }

        ConcurrencyToken version = await _connections
            .UpdateAsync(found.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return await PublishAsync(
                tenantId,
                new Versioned<WhatsAppConnection>(found.Value, version),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a saved connection into what a caller is allowed to see.
    /// </summary>
    /// <remarks>
    /// One place, reached by every read and every write, so "what leaves this
    /// feature" is a single method to review rather than six response shapes to
    /// keep in step.
    /// </remarks>
    private async Task<Versioned<WhatsAppConnectionView>> PublishAsync(
        TenantId tenantId,
        Versioned<WhatsAppConnection> saved,
        CancellationToken cancellationToken)
    {
        WhatsAppConnection connection = saved.Value;

        string? agentName = await _connections
            .FindAgentNameAsync(tenantId, connection.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<WhatsAppConnectionView>(
            new WhatsAppConnectionView(
                connection.Id.Value,
                connection.AiAgentId.Value,
                agentName,
                connection.WabaId.Value,
                connection.PhoneNumberId.Value,
                connection.DisplayPhoneNumber,
                connection.Status,
                connection.HasCredential,
                connection.CreatedAt,
                connection.UpdatedAt),
            saved.Version);
    }

    private async Task<Versioned<WhatsAppConnection>?> LoadForWriteAsync(
        TenantId tenantId,
        Guid connectionId,
        ConcurrencyToken expected,
        CancellationToken cancellationToken)
    {
        if (!await ManageAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Versioned<WhatsAppConnection>? found = await _connections
            .FindAsync(tenantId, WhatsAppConnectionId.From(connectionId), cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        return found.Version == expected
            ? found
            : throw new WhatsAppConnectionVersionMismatchException(expected);
    }

    /// <summary>
    /// The agent, if it belongs to this workspace.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to a foreign key, so an administrator is
    /// told which thing was wrong instead of meeting a constraint violation.
    /// </remarks>
    private async Task<AiAgentId> RequireAgentAsync(
        TenantId tenantId,
        Guid aiAgentId,
        CancellationToken cancellationToken)
    {
        AiAgentId id = AiAgentId.From(aiAgentId);

        Versioned<AiAgent>? agent = await _agents
            .FindAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return agent is null
            ? throw new DomainValidationException(
                "That AI agent does not belong to this workspace.")
            : id;
    }

    private Task<bool> ViewAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(tenantId, PlatformPermissions.WhatsAppView, cancellationToken);

    /// <summary>
    /// The permission every write here needs, and the only one.
    /// </summary>
    /// <remarks>
    /// <strong>Managing does not require viewing.</strong> No permission on this
    /// platform implies another: what somebody may do is the union of what their
    /// roles grant, readable from the roles themselves without evaluating a
    /// closure. Asking for both here would quietly make <c>whatsapp.view</c> a
    /// prerequisite for <c>whatsapp.manage</c> — a rule written in one handler
    /// that nothing in the catalogue records.
    /// </remarks>
    private Task<bool> ManageAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(tenantId, PlatformPermissions.WhatsAppManage, cancellationToken);
}
