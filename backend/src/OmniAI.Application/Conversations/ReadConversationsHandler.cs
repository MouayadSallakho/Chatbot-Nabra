using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>One page of a workspace's conversations.</summary>
public sealed record ListConversationsQuery(Guid TenantId, int? Page, int? PageSize);

/// <summary>One conversation of a workspace.</summary>
public sealed record GetConversationQuery(Guid TenantId, Guid ConversationId);

/// <summary>One page of a conversation's transcript.</summary>
public sealed record ListConversationMessagesQuery(
    Guid TenantId,
    Guid ConversationId,
    long? AfterSequence,
    int? Limit);

/// <summary>The bounds on reading conversations from the admin.</summary>
public sealed class ConversationReadPolicy
{
    /// <summary>The most conversations returned in one page.</summary>
    public int MaxPageSize { get; init; } = 50;

    /// <summary>The page size used when the caller does not ask for one.</summary>
    public int DefaultPageSize { get; init; } = 20;

    /// <summary>The most messages returned in one page of a transcript.</summary>
    /// <remarks>
    /// A conversation has no upper size, so an unbounded transcript read would
    /// eventually be a request that never finishes and a response no browser can
    /// render. Two hundred is several screens of history and a bounded query.
    /// </remarks>
    public int MaxMessagePageSize { get; init; } = 200;

    /// <summary>The transcript page size used when the caller does not ask for one.</summary>
    public int DefaultMessagePageSize { get; init; } = 100;
}

/// <summary>
/// Reading conversations, for people who administer the workspace.
/// </summary>
/// <remarks>
/// <strong>Still read-only, and still the whole capability of this type.</strong>
/// Replying, taking over and routing now exist, and each is its own use case with
/// its own permission and its own conditional write. They are deliberately not
/// methods here: a reader needs <c>conversations.view</c> and nothing else, and
/// keeping the read path free of every write concern is what makes that easy to
/// see. There is still no note, no close and no delete, because none of those
/// workflows exists.
/// <para>
/// <strong>Every operation names the tenant, and the predicate carries it.</strong>
/// A conversation id from another workspace is not a shortcut into it: the lookup
/// is scoped, so it simply finds nothing. That is checked before any transcript is
/// read, so a foreign id never causes a single message to be loaded.
/// </para>
/// <para>
/// <strong>The visitor's credential is nowhere in this file.</strong> Admin access
/// is membership plus <c>conversations.view</c>, resolved through
/// <c>TenantAuthorizer</c> exactly as everywhere else. Holding a conversation's
/// anonymous secret contributes nothing here, and there is no parameter it could
/// be supplied as.
/// </para>
/// </remarks>
public sealed class ReadConversationsHandler(
    IConversationRepository conversations,
    TenantAuthorizer authorizer,
    ConversationReadPolicy policy)
{
    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ConversationReadPolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    /// <returns>
    /// The page, or <see langword="null"/> when the caller does not belong to this
    /// workspace or lacks <c>conversations.view</c>.
    /// </returns>
    public async Task<ConversationPage?> ListAsync(
        ListConversationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await AuthoriseAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int page = Math.Max(query.Page ?? 1, 1);
        int pageSize = Math.Clamp(
            query.PageSize ?? _policy.DefaultPageSize,
            1,
            _policy.MaxPageSize);

        return await _conversations
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns>
    /// The conversation, or <see langword="null"/> when the caller is not entitled
    /// or no such conversation belongs to this workspace. Deliberately the same
    /// answer for both.
    /// </returns>
    public async Task<Versioned<ConversationDetail>?> GetAsync(
        GetConversationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        ConversationId conversationId = ConversationId.From(query.ConversationId);

        if (!await AuthoriseAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await _conversations
            .FindAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <returns>
    /// A page of the transcript, or <see langword="null"/> when the caller is not
    /// entitled or the conversation is not this workspace's.
    /// </returns>
    /// <remarks>
    /// Ownership is settled before any message is read, so a foreign conversation
    /// id cannot become an alternate route into another workspace's transcripts.
    /// </remarks>
    public async Task<ConversationMessagePage?> ListMessagesAsync(
        ListConversationMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        ConversationId conversationId = ConversationId.From(query.ConversationId);

        if (!await AuthoriseAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!await _conversations
            .BelongsToAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        int limit = Math.Clamp(
            query.Limit ?? _policy.DefaultMessagePageSize,
            1,
            _policy.MaxMessagePageSize);

        return await _conversations
            .ListMessagesAsync(conversationId, query.AfterSequence, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<bool> AuthoriseAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(
            tenantId,
            PlatformPermissions.ConversationsView,
            cancellationToken);
}
