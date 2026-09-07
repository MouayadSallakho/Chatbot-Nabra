using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// One anonymous visitor's claim on one conversation.
/// </summary>
/// <remarks>
/// <strong>This is what makes a public conversation private.</strong> A
/// conversation id is not a credential: it travels in URLs, appears in an
/// administrator's list, and is guessable to anybody who has seen one. Without a
/// separate secret, knowing or guessing an id would be enough to read a
/// stranger's transcript and to talk in their conversation on the workspace's
/// provider budget. This record holds the digest of a secret the visitor's
/// browser presents, and it is the only thing that grants access to a public
/// conversation.
/// <para>
/// <strong>Its authority stops at exactly one conversation.</strong> There is no
/// list of conversations here, no user, no membership and no permission. A
/// credential that matches this row lets its holder read and continue this
/// conversation and nothing else — not the workspace, not another conversation of
/// the same visitor, not the agent's instructions. Admin authorisation never
/// consults it, and it never satisfies admin authorisation.
/// </para>
/// <para>
/// <strong>It records the agent, not just the endpoint.</strong> This is the
/// stable binding: the conversation answers from the agent it started with, read
/// from here rather than followed through the endpoint. Re-pointing an endpoint
/// at another agent — whenever that becomes possible — therefore cannot move a
/// conversation already under way, and cannot make one visitor's history look
/// like it came from an agent that never saw it.
/// </para>
/// <para>
/// <strong>One session per conversation.</strong> The conversation is the
/// identity. A second visitor cannot attach to somebody else's conversation
/// because there is nowhere to put a second claim on it.
/// </para>
/// <para>
/// <strong>The claim expires, and its expiry is a stored fact.</strong> A
/// credential that never expired would be good for as long as the row existed,
/// so a copy taken from a shared computer, a proxy log or a browser profile
/// would keep working indefinitely. <see cref="ExpiresAt"/> is written when the
/// session opens rather than computed from configuration at each check, which
/// means changing the platform's lifetime setting cannot retroactively extend --
/// or retroactively revoke -- a claim somebody is already relying on.
/// </para>
/// <para>
/// <strong>The lifetime is absolute, never sliding.</strong> It is deliberately
/// not extended by activity. A sliding expiry would mean whoever holds a stolen
/// credential can keep it alive forever simply by using it, which is precisely
/// the case the expiry exists for.
/// </para>
/// </remarks>
public sealed class WebChatSession
{
    private WebChatSession(
        ConversationId conversationId,
        TenantId tenantId,
        WebChatEndpointId endpointId,
        AiAgentId aiAgentId,
        WebChatAccessTokenHash accessTokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ConversationId = conversationId;
        TenantId = tenantId;
        EndpointId = endpointId;
        AiAgentId = aiAgentId;
        AccessTokenHash = accessTokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The conversation this claim is on. Also its identity.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>
    /// The owning workspace, carried so a public request never has to be told one.
    /// </summary>
    /// <remarks>
    /// The visitor supplies no tenant and could not be trusted with one. The
    /// workspace is read from here, which is why a public caller cannot reach
    /// across workspaces however they shape their request.
    /// </remarks>
    public TenantId TenantId { get; }

    /// <summary>The endpoint the visitor arrived through.</summary>
    public WebChatEndpointId EndpointId { get; }

    /// <summary>The agent that answers in this conversation, for its lifetime.</summary>
    public AiAgentId AiAgentId { get; }

    /// <summary>The digest of the visitor's credential. The raw secret is never here.</summary>
    public WebChatAccessTokenHash AccessTokenHash { get; }

    /// <summary>When the visitor started, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The instant this claim stops opening the conversation, in UTC.
    /// </summary>
    /// <remarks>
    /// Decided once, when the session opened, and stored. Reading it back rather
    /// than recomputing it is what makes the expiry a fact about this session
    /// instead of a fact about the current configuration.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Opens an anonymous claim on a newly started conversation.</summary>
    /// <exception cref="DomainValidationException">
    /// An identifier is empty, the digest is missing, or the instant is not a
    /// real point in time.
    /// </exception>
    public static WebChatSession Open(
        ConversationId conversationId,
        TenantId tenantId,
        WebChatEndpointId endpointId,
        AiAgentId aiAgentId,
        WebChatAccessTokenHash accessTokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (conversationId.IsEmpty)
        {
            throw new DomainValidationException("A web chat session must name a conversation.");
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A web chat session must belong to a workspace.");
        }

        if (endpointId.IsEmpty)
        {
            throw new DomainValidationException("A web chat session must name an endpoint.");
        }

        if (aiAgentId.IsEmpty)
        {
            throw new DomainValidationException("A web chat session must name an AI agent.");
        }

        ArgumentNullException.ThrowIfNull(accessTokenHash);

        if (createdAt == default)
        {
            throw new DomainValidationException("The creation instant must be a real point in time.");
        }

        // A session that expires when or before it opens would be a credential
        // that never worked, which is a configuration mistake worth refusing
        // loudly rather than a conversation nobody can continue.
        if (expiresAt <= createdAt)
        {
            throw new DomainValidationException(
                "A web chat session must expire after it was created.");
        }

        return new WebChatSession(
            conversationId,
            tenantId,
            endpointId,
            aiAgentId,
            accessTokenHash,
            createdAt.ToUniversalTime(),
            expiresAt.ToUniversalTime());
    }

    /// <summary>
    /// Whether the secret behind this digest is the one that opened the session.
    /// </summary>
    /// <remarks>
    /// The comparison is the digest type's, which is constant-time. Written as a
    /// method on the session rather than left to callers so that every access
    /// check in the platform is spelled the same way and cannot accidentally
    /// become a short-circuiting <c>SequenceEqual</c>.
    /// </remarks>
    public bool Admits(WebChatAccessTokenHash? presented) =>
        presented is not null && AccessTokenHash == presented;

    /// <summary>
    /// Whether this claim has run out at the given instant.
    /// </summary>
    /// <remarks>
    /// <strong>Separate from <see cref="Admits"/> on purpose.</strong> Holding
    /// the right secret and holding it in time are two different questions, and
    /// answering them separately is what lets the access check refuse an expired
    /// session with exactly the same answer it gives an unknown conversation.
    /// Folding the clock into <c>Admits</c> would also mean a constant-time
    /// digest comparison had a time-dependent result, which is a confusing thing
    /// for a security primitive to be.
    /// <para>
    /// The instant is passed in rather than read from a clock here, because the
    /// Domain owns no clock: whoever asks decides what "now" is, and a test can
    /// stand on either side of the boundary without waiting a day.
    /// </para>
    /// </remarks>
    public bool HasExpired(DateTimeOffset asOf) => asOf >= ExpiresAt;
}
