using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>One page of a workspace's conversations.</summary>
/// <param name="Filter">
/// <see langword="null"/> or empty for every conversation; <c>needs-person</c>
/// for the ones waiting on a person (Phase 37.4); <c>mine</c> for the ones
/// assigned to or taken over by the reader (Phase 39.1). Anything else is refused.
/// </param>
public sealed record ListConversationsQuery(
    Guid TenantId,
    int? Page,
    int? PageSize,
    string? Filter = null,
    string? Search = null,
    InboxFilters? Criteria = null);

/// <summary>One conversation of a workspace.</summary>
public sealed record GetConversationQuery(Guid TenantId, Guid ConversationId);

/// <summary>One page of a conversation's transcript.</summary>
/// <param name="BeforeSequence">
/// Read backwards: the messages before this sequence (Phase 37.4).
/// </param>
/// <param name="Tail">Read backwards from the newest message (Phase 37.4).</param>
/// <param name="Include">
/// A comma-separated list of extras; only <c>events</c> exists (Phase 37.4).
/// </param>
public sealed record ListConversationMessagesQuery(
    Guid TenantId,
    Guid ConversationId,
    long? AfterSequence,
    int? Limit,
    long? BeforeSequence = null,
    bool Tail = false,
    string? Include = null);

/// <summary>A page of a transcript, for the workspace's own screens.</summary>
/// <param name="Items">Messages in ingestion order, oldest first, in every reading direction.</param>
/// <param name="NextCursor">The sequence to continue forwards after, or null at the newest end.</param>
/// <param name="PreviousCursor">The sequence to read the earlier page before, or null at the start.</param>
/// <param name="Events">
/// Failed AI turns, takeovers and returns to the AI in this page's span, when
/// <c>include=events</c> asked for them; otherwise <see langword="null"/>.
/// </param>
public sealed record ConversationTranscriptPage(
    IReadOnlyList<ConversationMessageRecord> Items,
    long? NextCursor,
    long? PreviousCursor,
    IReadOnlyList<TranscriptEvent>? Events);

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
    ConversationReadPolicy policy,
    TimeProvider? clock = null)
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

        // Refused before anything is authorised or read, like a malformed cursor:
        // the answer is the same whoever asks, so it says nothing about the
        // workspace.
        ConversationListFilter filter = ParseFilter(query.Filter);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await AuthoriseAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string? search = query.Search?.Trim();
        if (search is { Length: > 120 } || search?.Contains('\0') == true)
        {
            throw ConversationQueryInvalidException.InvalidSearch();
        }

        if (query.Criteria is { } criteria &&
            ((criteria.Channel is { } channel && !Enum.IsDefined(channel)) ||
             (criteria.Status is { } status && !Enum.IsDefined(status)) ||
             criteria.AssignedTo == Guid.Empty || (criteria.AssignedTo.HasValue && criteria.Unassigned) ||
             criteria.ActiveFrom >= criteria.ActiveBefore))
        {
            throw ConversationQueryInvalidException.InvalidCriteria();
        }

        int page = Math.Max(query.Page ?? 1, 1);
        int pageSize = Math.Clamp(
            query.PageSize ?? _policy.DefaultPageSize,
            1,
            _policy.MaxPageSize);

        return await _conversations
            .ListAsync(
                tenantId,
                page,
                pageSize,
                filter,
                _authorizer.RequireCurrentUser(),
                search,
                query.Criteria,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationMessageAuthor>?> AssignedMembersAsync(
        Guid workspace, CancellationToken cancellationToken)
    {
        TenantId tenantId = TenantId.From(workspace);
        return await AuthoriseAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ? await _conversations.ListAssignedMembersAsync(tenantId, cancellationToken).ConfigureAwait(false)
            : null;
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

        var detail = await _conversations
            .FindAsync(tenantId, conversationId, _authorizer.RequireCurrentUser(), cancellationToken)
            .ConfigureAwait(false);
        if (detail?.Value.Channel != ChannelKind.WhatsApp)
        {
            return detail;
        }

        var last = await _conversations.LastCustomerMessageAtAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var window = new WhatsApp.CustomerServiceWindow(clock ?? TimeProvider.System).Describe(last);
        return detail with { Value = detail.Value with { ReplyWindow = window } };
    }

    /// <returns>
    /// A page of the transcript, or <see langword="null"/> when the caller is not
    /// entitled or the conversation is not this workspace's.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ownership is settled before any message is read, so a foreign conversation
    /// id cannot become an alternate route into another workspace's transcripts.
    /// </para>
    /// <para>
    /// <strong>Forwards exactly as before.</strong> Without <c>before</c> or
    /// <c>tail</c> the page is the one this endpoint always served. Reading from
    /// the newest end is opt-in, and its pages are ascending inside too, so a
    /// client merging pages by sequence needs no change.
    /// </para>
    /// <para>
    /// <strong>The span a page owns for events.</strong> From its first message
    /// (or the start of the conversation when nothing is earlier; or the
    /// <c>after</c> cursor, so a client polling forwards sees what happened after
    /// the last message it holds) to its last message (or the end when nothing is
    /// later), both inclusive. An event exactly on a forward page's boundary can
    /// therefore come with both pages; its id says it is one event.
    /// </para>
    /// </remarks>
    /// <exception cref="ConversationQueryInvalidException">
    /// <c>after</c> with <c>before</c> or <c>tail</c>, or an unknown <c>include</c>.
    /// </exception>
    public async Task<ConversationTranscriptPage?> ListMessagesAsync(
        ListConversationMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        bool backwards = query.Tail || query.BeforeSequence is not null;

        // Refused before anything is authorised or read: a request that asks to
        // go both ways at once has no answer, whoever sends it.
        if (backwards && query.AfterSequence is not null)
        {
            throw ConversationQueryInvalidException.CursorConflict();
        }

        bool includeEvents = ParseInclude(query.Include);

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

        ConversationMessagePage page;
        long from;
        long to;

        if (backwards)
        {
            // tail=true with a before cursor reads before the cursor: before is
            // the more specific of the two.
            page = await _conversations
                .ListMessagesBeforeAsync(conversationId, query.BeforeSequence, limit, cancellationToken)
                .ConfigureAwait(false);

            from = page.PreviousCursor is null ? long.MinValue : page.Items[0].IngestionSequence;
            to = page.Items.Count == 0
                ? (query.BeforeSequence is long before ? before - 1 : long.MaxValue)
                : page.NextCursor is null ? long.MaxValue : page.Items[^1].IngestionSequence;
        }
        else
        {
            page = await _conversations
                .ListMessagesAsync(conversationId, query.AfterSequence, limit, cancellationToken)
                .ConfigureAwait(false);

            // Only a page read after a cursor can have anything before it; the
            // first page of a conversation never does, and asks nothing more.
            long? previous = query.AfterSequence is not null
                && page.Items.Count > 0
                && await _conversations
                    .HasMessagesBeforeAsync(conversationId, page.Items[0].IngestionSequence, cancellationToken)
                    .ConfigureAwait(false)
                ? page.Items[0].IngestionSequence
                : null;

            page = page with { PreviousCursor = previous };

            from = query.AfterSequence ?? long.MinValue;
            to = page.NextCursor is null ? long.MaxValue : page.Items[^1].IngestionSequence;
        }

        IReadOnlyList<TranscriptEvent>? events = includeEvents
            ? await _conversations
                .ListTranscriptEventsAsync(conversationId, from, to, cancellationToken)
                .ConfigureAwait(false)
            : null;

        return new ConversationTranscriptPage(page.Items, page.NextCursor, page.PreviousCursor, events);
    }

    /// <summary>Reads the list filter; an unknown one is refused, never ignored.</summary>
    /// <remarks>
    /// Refused rather than treated as "no filter": a client that misspelt it would
    /// otherwise show every conversation under a heading that promises a few.
    /// </remarks>
    private static ConversationListFilter ParseFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? ConversationListFilter.All
            : string.Equals(filter.Trim(), "needs-person", StringComparison.OrdinalIgnoreCase)
                ? ConversationListFilter.NeedsPerson
                : string.Equals(filter.Trim(), "mine", StringComparison.OrdinalIgnoreCase)
                    ? ConversationListFilter.Mine
                    : string.Equals(filter.Trim(), "overdue", StringComparison.OrdinalIgnoreCase)
                        ? ConversationListFilter.Overdue : throw ConversationQueryInvalidException.UnknownFilter();

    /// <summary>Reads <c>include</c>: a comma-separated list whose only member is <c>events</c>.</summary>
    private static bool ParseInclude(string? include)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return false;
        }

        bool events = false;

        foreach (string part in include.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            events = string.Equals(part, "events", StringComparison.OrdinalIgnoreCase)
                ? true
                : throw ConversationQueryInvalidException.UnknownInclude();
        }

        return events;
    }

    private Task<bool> AuthoriseAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(
            tenantId,
            PlatformPermissions.ConversationsView,
            cancellationToken);
}
