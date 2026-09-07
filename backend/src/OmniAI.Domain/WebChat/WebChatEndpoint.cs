using System.Globalization;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// One workspace's hosted website chat, and the AI agent behind it.
/// </summary>
/// <remarks>
/// <strong>A channel configuration, not a property of the agent.</strong> An
/// <c>AiAgent</c> is what the business has taught its AI to do; where that agent
/// is reachable from is a different question with a different lifecycle. Putting
/// a public key and a channel status onto <c>AiAgent</c> would have meant every
/// future channel adding its own fields to the same aggregate, and an agent's
/// version changing because a channel was switched off.
/// <para>
/// <strong>Deliberately almost empty.</strong> There is no branding, no colour,
/// no logo, no greeting text, no launcher position, no custom CSS and no domain
/// allowlist. Those are real website-chat product features and every one of them
/// is presentation; this phase is about whether the runtime behind the page is
/// correct. Adding a settings bag now would fix a shape before anybody knows what
/// belongs in it.
/// </para>
/// <para>
/// <strong>One agent, and the binding is the point.</strong> An endpoint names
/// exactly one agent. A conversation started here records that agent itself
/// rather than following the endpoint, so re-pointing an endpoint later — a
/// capability this phase does not even offer — could never move a conversation
/// already in progress onto a different agent mid-sentence.
/// </para>
/// </remarks>
public sealed class WebChatEndpoint
{
    private WebChatEndpoint(
        WebChatEndpointId id,
        TenantId tenantId,
        AiAgentId aiAgentId,
        WebChatPublicKey publicKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        AiAgentId = aiAgentId;
        PublicKey = publicKey;
        Status = WebChatEndpointStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Identity. Fixed for the lifetime of the endpoint.</summary>
    public WebChatEndpointId Id { get; }

    /// <summary>
    /// The owning workspace. No setter of any kind, as everywhere else: tenant
    /// ownership is the isolation boundary, and an endpoint that could be moved
    /// between workspaces would be a way to move a public URL between them too.
    /// </summary>
    public TenantId TenantId { get; }

    /// <summary>
    /// The agent that answers here. Fixed in this phase.
    /// </summary>
    /// <remarks>
    /// Changing it is not offered, and that is a decision rather than an
    /// omission: re-pointing a live endpoint changes what every future visitor
    /// talks to, and it needs its own deliberate operation with its own
    /// confirmation. Conversations already record their own agent, so adding the
    /// capability later cannot disturb them.
    /// </remarks>
    public AiAgentId AiAgentId { get; }

    /// <summary>The opaque key in the hosted page's address. Not a secret.</summary>
    public WebChatPublicKey PublicKey { get; }

    /// <summary>Current lifecycle state.</summary>
    public WebChatEndpointStatus Status { get; private set; }

    /// <summary>When the endpoint was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When the endpoint last changed, in UTC. Never moves backwards.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this endpoint may serve live public conversations.</summary>
    /// <remarks>
    /// The single question every public request asks. Expressed here rather than
    /// as a status comparison spread across handlers, so adding a state later
    /// cannot silently start serving from it.
    /// </remarks>
    public bool IsServingPublicTraffic => Status == WebChatEndpointStatus.Active;

    /// <summary>Configures a website chat endpoint. It begins as a draft.</summary>
    /// <exception cref="DomainValidationException">
    /// An identifier is empty, the key is missing, or the instant is not a real
    /// point in time.
    /// </exception>
    public static WebChatEndpoint Create(
        TenantId tenantId,
        AiAgentId aiAgentId,
        WebChatPublicKey publicKey,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A web chat endpoint must belong to a workspace.");
        }

        if (aiAgentId.IsEmpty)
        {
            throw new DomainValidationException("A web chat endpoint must name an AI agent.");
        }

        if (publicKey.IsEmpty)
        {
            throw new DomainValidationException("A web chat endpoint must have a public key.");
        }

        return new WebChatEndpoint(
            WebChatEndpointId.New(),
            tenantId,
            aiAgentId,
            publicKey,
            RequireInstant(createdAt, "creation instant"));
    }

    /// <summary>
    /// Reconstitutes a stored endpoint. For persistence only.
    /// </summary>
    /// <remarks>
    /// Persistence must be able to rebuild a row exactly as it was stored,
    /// including a status the public factory never produces. It performs no
    /// lifecycle reasoning and raises nothing, because the row it is rebuilding
    /// was already validated when it was written.
    /// </remarks>
    public static WebChatEndpoint FromStored(
        WebChatEndpointId id,
        TenantId tenantId,
        AiAgentId aiAgentId,
        WebChatPublicKey publicKey,
        WebChatEndpointStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, tenantId, aiAgentId, publicKey, createdAt)
        {
            Status = status,
            UpdatedAt = updatedAt,
        };

    /// <summary>
    /// Puts the endpoint into service. Idempotent.
    /// </summary>
    /// <remarks>
    /// Idempotent for the reason <c>Conversation.Close</c> is: the caller's intent
    /// is already satisfied, and demanding a check-then-act sequence would invite
    /// a race while preventing nothing. An activation that changes nothing does
    /// not move <see cref="UpdatedAt"/> either, so it cannot be used to churn a
    /// version other requests are holding preconditions against.
    /// </remarks>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Activate(DateTimeOffset activatedAt)
    {
        DateTimeOffset instant = RequireInstant(activatedAt, "activation instant");

        if (Status == WebChatEndpointStatus.Active)
        {
            return;
        }

        Status = WebChatEndpointStatus.Active;
        RecordChange(instant);
    }

    /// <summary>
    /// Withdraws the endpoint from service. Idempotent.
    /// </summary>
    /// <remarks>
    /// Existing transcripts are not touched. A visitor who already holds a
    /// conversation credential keeps reading their own history; what stops is new
    /// conversations and new turns. Deleting or hiding what was already said would
    /// be a retention decision, and this is a service decision.
    /// </remarks>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Disable(DateTimeOffset disabledAt)
    {
        DateTimeOffset instant = RequireInstant(disabledAt, "disable instant");

        if (Status == WebChatEndpointStatus.Disabled)
        {
            return;
        }

        Status = WebChatEndpointStatus.Disabled;
        RecordChange(instant);
    }

    /// <remarks>
    /// A high-water mark, consistent with every other aggregate here: an earlier
    /// instant is ignored rather than written, so two administrators saving within
    /// the same tick cannot make the endpoint look older than it is.
    /// </remarks>
    private void RecordChange(DateTimeOffset instant)
    {
        if (instant > UpdatedAt)
        {
            UpdatedAt = instant;
        }
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant, string what)
    {
        if (instant == default)
        {
            throw new DomainValidationException($"The {what} must be a real point in time.");
        }

        return instant.ToUniversalTime();
    }
}
