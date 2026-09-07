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
/// </remarks>
public sealed class WebChatSession
{
    private WebChatSession(
        ConversationId conversationId,
        TenantId tenantId,
        WebChatEndpointId endpointId,
        AiAgentId aiAgentId,
        WebChatAccessTokenHash accessTokenHash,
        DateTimeOffset createdAt)
    {
        ConversationId = conversationId;
        TenantId = tenantId;
        EndpointId = endpointId;
        AiAgentId = aiAgentId;
        AccessTokenHash = accessTokenHash;
        CreatedAt = createdAt;
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
        DateTimeOffset createdAt)
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

        return new WebChatSession(
            conversationId,
            tenantId,
            endpointId,
            aiAgentId,
            accessTokenHash,
            createdAt.ToUniversalTime());
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
}
