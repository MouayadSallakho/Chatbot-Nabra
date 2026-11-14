using OmniAI.Application.Concurrency;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Customers.Persistence;

/// <summary>
/// The aggregated facts a directory row needs, computed by the database.
/// </summary>
/// <remarks>
/// A row rather than an aggregate, because every field but the label is a count
/// or a maximum over conversations. Loading the customers and then counting their
/// conversations in memory would be one query per customer and would read every
/// conversation in the workspace to answer a page of twenty.
/// </remarks>
/// <param name="Reference">
/// The customer's most recently created WhatsApp number, masked to its last four
/// digits (<c>••••1234</c>), or null for a customer without one (Phase 39.1). Set
/// by the list read; masked on the value object, never carried unmasked.
/// </param>
public sealed record CustomerDirectoryRow(
    CustomerId Id,
    CustomerDisplayName? DisplayName,
    int ConversationCount,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ChannelKind> Channels,
    string? Reference = null);

/// <summary>What a merge moved.</summary>
public sealed record CustomerMergeCounts(int Conversations, int Identities);

/// <summary>
/// Reading and writing the canonical customer directory.
/// </summary>
/// <remarks>
/// <strong>The merge is one method, not a sequence of them.</strong> Moving the
/// identities, moving the conversations and tombstoning the source are one
/// transaction with two rows locked for its duration, and splitting them across
/// the interface would let a caller perform half a merge.
/// </remarks>
public interface ICustomerRepository
{
    /// <summary>
    /// One page of canonical customers, newest activity first.
    /// </summary>
    /// <remarks>
    /// Merged records are excluded. The total is the number of canonical
    /// customers, so it matches what the caller can actually page through.
    /// </remarks>
    Task<(IReadOnlyList<CustomerDirectoryRow> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One customer of this workspace, with the version a change must present
    /// back.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> for a customer that does not exist and for one
    /// belonging to another workspace, indistinguishably.
    /// </returns>
    Task<Versioned<Customer>?> FindAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default);

    /// <summary>The aggregated conversation facts for one customer.</summary>
    Task<CustomerDirectoryRow?> SummariseAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every channel identity pointing at one customer, masked.
    /// </summary>
    /// <remarks>
    /// Masking happens in the Domain value object, so there is no code path here
    /// that could return a raw number by forgetting to call something.
    /// </remarks>
    Task<IReadOnlyList<CustomerChannelIdentityView>> ListIdentitiesAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One customer's conversations, newest activity first, bounded.
    /// </summary>
    /// <remarks>
    /// Metadata only. Nothing in the shape can carry message text.
    /// </remarks>
    Task<IReadOnlyList<CustomerConversationView>> ListConversationsAsync(
        TenantId tenantId,
        CustomerId customerId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a changed customer, if it is still at this version.</summary>
    /// <exception cref="CustomerVersionMismatchException">
    /// Somebody else changed it in between.
    /// </exception>
    Task<ConcurrencyToken> UpdateAsync(
        Customer customer,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Combines two customers in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both rows are locked before anything is read, in a fixed order derived
    /// from their ids, so two merges naming the same pair from opposite
    /// directions queue behind each other instead of deadlocking. The canonical
    /// state of both is re-read while the locks are held, because the decision to
    /// merge was made against a version that may since have changed.
    /// </para>
    /// <para>
    /// Conversations and identities move by set-based update. Loading every
    /// conversation a person ever had in order to change one column would make
    /// the cost of a merge proportional to the history it preserves.
    /// </para>
    /// </remarks>
    /// <returns>What moved, and whether this merge had already happened.</returns>
    /// <exception cref="CustomerMergeNotAllowedException">
    /// Same record, different workspaces, or a target that is itself merged.
    /// </exception>
    /// <exception cref="CustomerAlreadyMergedException">
    /// The source is already merged into a different customer.
    /// </exception>
    /// <exception cref="CustomerVersionMismatchException">
    /// The source changed since the caller read it.
    /// </exception>
    Task<(CustomerMergeCounts Counts, bool AlreadyMerged)> MergeAsync(
        TenantId tenantId,
        CustomerId sourceId,
        CustomerId targetId,
        UserId mergedBy,
        ConcurrencyToken expected,
        DateTimeOffset mergedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when a customer was changed between being read and being written.
/// </summary>
/// <remarks>
/// Its own type rather than a shared one, for the reason recorded on
/// <c>Versioned&lt;T&gt;</c>: a shared exception would have to describe a
/// customer and a role in the same sentence.
/// </remarks>
public sealed class CustomerVersionMismatchException()
    : Exception(
        "This customer was changed by somebody else. Reload it and apply your change again.");
