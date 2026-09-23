using OmniAI.Application.Concurrency;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Customers;

/// <summary>One page of a workspace's customer directory.</summary>
public sealed record ListCustomersQuery(Guid TenantId, int? Page, int? PageSize);

/// <summary>One customer.</summary>
public sealed record GetCustomerQuery(Guid TenantId, Guid CustomerId);

/// <summary>Set or clear a customer's label.</summary>
/// <param name="DisplayName">
/// The new label, or <see langword="null"/>/blank to remove one. Clearing is a
/// real operation: a colleague who wrote somebody's name into the wrong record
/// needs a way to take it out again.
/// </param>
public sealed record RenameCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    string? DisplayName,
    ConcurrencyToken Expected);

/// <summary>
/// Combine two customers, keeping the target.
/// </summary>
/// <remarks>
/// The source's channel identities and conversation history move to the target
/// and the source becomes a tombstone pointing at it. Nothing is deleted, and
/// this phase has no way to undo it.
/// </remarks>
public sealed record MergeCustomersCommand(
    Guid TenantId,
    Guid SourceCustomerId,
    Guid TargetCustomerId,
    ConcurrencyToken Expected);

/// <summary>
/// One way a customer reaches this workspace, in a form safe to show.
/// </summary>
/// <param name="Reference">
/// <strong>Already masked, and masked in the Domain.</strong> A WhatsApp number
/// leaves as its last four digits, which is enough for a colleague to recognise
/// who they are talking to and not enough for a screenshot to identify a
/// stranger. There is no unmasked variant of this field and no flag that
/// produces one.
/// </param>
public sealed record CustomerChannelIdentityView(ChannelKind Channel, string Reference);

/// <summary>
/// One conversation of a customer's, as the detail page lists it.
/// </summary>
/// <remarks>
/// <strong>Metadata only, and only for a caller who may read conversations.</strong>
/// There is no message text here and no field that could carry any: the customer
/// directory must not become a way around the permission that guards transcripts.
/// </remarks>
public sealed record CustomerConversationView(
    Guid ConversationId,
    ChannelKind Channel,
    ConversationStatus Status,
    ConversationHandling Handling,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt);

/// <summary>One row of the directory.</summary>
/// <param name="Label">
/// What to show. The customer's own label when they have one, and otherwise a
/// safe description built from how they reach this workspace — never a name
/// invented from anything they said.
/// </param>
/// <param name="Channels">The channels this customer has actually used.</param>
public sealed record CustomerRow(
    Guid Id,
    string? DisplayName,
    string Label,
    IReadOnlyList<ChannelKind> Channels,
    int ConversationCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// One page of the directory.
/// </summary>
/// <remarks>
/// Merged records are not in it. They are still readable one at a time — an old
/// link has to resolve to something — but a directory that listed both halves of
/// every merge would undo the merge visually.
/// </remarks>
public sealed record CustomerPage(
    IReadOnlyList<CustomerRow> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Page size used when the caller does not choose one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The largest page anybody may ask for.
    /// </summary>
    /// <remarks>
    /// The same ceiling the agent listing uses. A directory is the one screen
    /// somebody is tempted to ask for "all of" so they can filter in the browser,
    /// and a workspace with a hundred thousand customers would answer that
    /// request by reading a hundred thousand rows.
    /// </remarks>
    public const int MaxPageSize = 100;
}

/// <summary>
/// One customer, in full.
/// </summary>
/// <param name="Conversations">
/// Empty when the caller may not read conversations. The count and the last
/// activity are still reported: knowing that somebody has fifteen conversations
/// is not the same as being able to read one, and hiding it would make the
/// directory useless to anybody who administers customers without reading mail.
/// </param>
/// <param name="CanReadConversations">
/// Whether <paramref name="Conversations"/> is empty because there are none, or
/// because this caller may not see them. Without it the interface would have to
/// guess, and would guess wrong for a new customer.
/// </param>
/// <param name="MergedIntoCustomerId">
/// Set when this record is a tombstone. A caller that followed an old link is
/// told which record is canonical rather than being refused.
/// </param>
public sealed record CustomerView(
    Guid Id,
    string? DisplayName,
    string Label,
    IReadOnlyList<CustomerChannelIdentityView> Identities,
    IReadOnlyList<ChannelKind> Channels,
    int ConversationCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? MergedIntoCustomerId,
    DateTimeOffset? MergedAt,
    IReadOnlyList<CustomerConversationView> Conversations,
    bool CanReadConversations);

/// <summary>
/// What a merge did.
/// </summary>
/// <param name="AlreadyMerged">
/// True when the source was already merged into this very target, so nothing
/// moved. Repeating a merge is not an error — a retried request, a double-click
/// and a stale tab all produce one — and answering "done" is the only response
/// that does not invite the caller to try something else.
/// </param>
public sealed record CustomerMergeResult(
    Guid SourceCustomerId,
    Guid TargetCustomerId,
    int ConversationsMoved,
    int IdentitiesMoved,
    bool AlreadyMerged);

/// <summary>Raised when a page number or size is outside what the directory serves.</summary>
public sealed class CustomerPageValidationException(string message) : Exception(message);
