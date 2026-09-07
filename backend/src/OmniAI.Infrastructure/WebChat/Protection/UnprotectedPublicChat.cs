using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.WebChat.Protection;

/// <summary>
/// What stands in for the protection store when this deployment has none.
/// </summary>
/// <remarks>
/// <strong>It refuses everything, and that is the correct behaviour.</strong> The
/// alternative implementations of "no Redis configured" are both worse: allowing
/// every request would mean a deployment that forgot one setting exposed an
/// unmetered, paid, anonymous surface to the internet, and counting in memory
/// would mean the platform quietly claimed protection it could not deliver across
/// instances. Refusing is loud, safe, and names the missing setting in the log.
/// <para>
/// <strong>It is the same shape as <c>UnconfiguredAiTextGenerator</c>, on
/// purpose.</strong> This platform has an established answer for a capability a
/// deployment has not configured: the application still starts, everything that
/// does not need the capability still works, and the capability itself fails
/// explicitly at its own boundary rather than being faked or taking the process
/// down. An operator sees a working admin, a working health probe, and a public
/// chat that says it is temporarily unavailable — which is exactly what it is.
/// </para>
/// <para>
/// What still works with this installed: signing in, the whole admin surface,
/// reading conversations, reading a chat endpoint's public face and reading a
/// transcript. What does not: starting a public conversation and sending a public
/// message — the two operations that create data and spend money.
/// </para>
/// </remarks>
internal sealed class UnprotectedPublicChat(string reason) : IPublicChatProtection
{
    private readonly string _reason = reason ?? throw new ArgumentNullException(nameof(reason));

    public Task<PublicChatDecision> AuthoriseConversationStartAsync(
        ClientNetworkKey client,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default) => Refuse();

    public Task<PublicChatDecision> AuthoriseRequestAsync(
        ClientNetworkKey client,
        CancellationToken cancellationToken = default) => Refuse();

    public Task<PublicChatDecision> AuthoriseGenerationAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default) => Refuse();

    /// <summary>
    /// Raised rather than returned as a rejected decision.
    /// </summary>
    /// <remarks>
    /// A rejected decision carries a retry hint and means "you are asking too
    /// often" — advice that would be a lie here, because waiting will not help
    /// until somebody changes configuration. This is unavailability, and the two
    /// must not be conflated: one is answered 429 and the other 503.
    /// </remarks>
    private Task<PublicChatDecision> Refuse() =>
        throw new PublicChatProtectionUnavailableException(_reason);
}
