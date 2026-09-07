using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.SetWebChatEndpointStatus;

/// <summary>Put a website chat endpoint into or out of service.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="WebChatEndpointId">The endpoint.</param>
/// <param name="Activate">
/// <see langword="true"/> to publish, <see langword="false"/> to withdraw.
/// </param>
/// <param name="Expected">The version the caller read the endpoint at.</param>
public sealed record SetWebChatEndpointStatusCommand(
    Guid TenantId,
    Guid WebChatEndpointId,
    bool Activate,
    ConcurrencyToken Expected);

/// <summary>
/// Activates or disables a website chat endpoint.
/// </summary>
/// <remarks>
/// <strong>One handler for both directions.</strong> They share every step —
/// the same permission, the same lookup, the same precondition, the same write —
/// and differ only in which aggregate method is called. Two handlers would have
/// been two copies of the security-relevant part, kept in step by hand.
/// <para>
/// <strong>Activating does not require the agent to be active.</strong> That is
/// checked on every public request instead, because an agent can be disabled at
/// any moment after this call and a check here would be a snapshot that stops
/// being true. The admin screen warns about a draft or disabled agent so nobody
/// publishes one by accident, but the guarantee that no disabled agent ever
/// answers the public lives in the runtime, where it holds.
/// </para>
/// </remarks>
public sealed class SetWebChatEndpointStatusHandler(
    IWebChatEndpointRepository endpoints,
    IAiAgentRepository agents,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The endpoint at its new version, or <see langword="null"/> when the caller
    /// is not entitled or no such endpoint belongs to this workspace.
    /// </returns>
    /// <exception cref="WebChatEndpointVersionMismatchException">
    /// Somebody else changed the endpoint first.
    /// </exception>
    public async Task<Versioned<WebChatEndpointView>?> HandleAsync(
        SetWebChatEndpointStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        WebChatEndpointId endpointId = WebChatEndpointId.From(command.WebChatEndpointId);

        // Before the lookup and before the precondition, so a valid entity tag for
        // somebody else's endpoint buys nothing, and a member without the
        // permission cannot use a 412 to discover which endpoint ids are real.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WebsiteChatManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<WebChatEndpoint>? stored = await _endpoints
            .FindAsync(tenantId, endpointId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Checked here as well as by the store, because an idempotent change
        // writes nothing and so would never reach a database predicate to fail
        // against -- activating an already active endpoint with a stale tag must
        // still be refused.
        if (stored.Version != command.Expected)
        {
            throw new WebChatEndpointVersionMismatchException(command.Expected);
        }

        if (command.Activate)
        {
            stored.Value.Activate(_clock.GetUtcNow());
        }
        else
        {
            stored.Value.Disable(_clock.GetUtcNow());
        }

        ConcurrencyToken version = await _endpoints
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        Versioned<AiAgent>? agent = await _agents
            .FindAsync(tenantId, stored.Value.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<WebChatEndpointView>(
            WebChatEndpointView.From(
                stored.Value,
                agent?.Value.Name.Value ?? "Unknown agent",
                agent?.Value.Status ?? AiAgentStatus.Disabled),
            version);
    }
}
