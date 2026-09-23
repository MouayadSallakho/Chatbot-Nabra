using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Customers;

/// <summary>
/// A person one workspace talks to, independent of how they reach it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is an identity foundation, not a CRM record.</strong> It holds
/// who somebody is to this workspace and nothing about what they are worth to
/// it: no notes, no tags, no stage, no score, no custom fields. Those are
/// products in their own right, and adding a column for one now would decide
/// their design by accident.
/// </para>
/// <para>
/// <strong>It deliberately holds no channel identifier.</strong> The phone number
/// lives with the WhatsApp mapping and the session credential lives with the web
/// chat session, because each belongs to the transport that issued it and each
/// has its own lifetime, masking rule and retention question. Copying them here
/// "for convenience" would create a second place to get personal data wrong.
/// </para>
/// <para>
/// <strong>Nothing infers identity.</strong> A customer is created when a
/// transport relationship begins, and two customers become one only when an
/// administrator says so. No name matching, no phone matching, no device or
/// network resemblance, no model. If the platform cannot prove two identities are
/// the same person from the relationship it already has, they stay two people.
/// </para>
/// <para>
/// <strong>Merging tombstones rather than deletes.</strong> The source keeps its
/// row and points at the canonical customer, so an old link, an old export and an
/// old screenshot all still resolve to a real answer. Nothing in this phase
/// deletes a customer or their history.
/// </para>
/// </remarks>
public sealed class Customer
{
    private Customer(
        CustomerId id,
        TenantId tenantId,
        CustomerDisplayName? displayName,
        CustomerId? mergedIntoCustomerId,
        DateTimeOffset? mergedAt,
        UserId? mergedBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        MergedIntoCustomerId = mergedIntoCustomerId;
        MergedAt = mergedAt;
        MergedBy = mergedBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Identity.</summary>
    public CustomerId Id { get; }

    /// <summary>
    /// The owning workspace, fixed for life.
    /// </summary>
    /// <remarks>
    /// There is no operation that moves a customer between workspaces, and there
    /// must not be: their conversations, their channel identities and the people
    /// allowed to read them all belong to one workspace, and moving the customer
    /// alone would leave every one of those pointing somewhere it may not.
    /// </remarks>
    public TenantId TenantId { get; }

    /// <summary>An optional human-friendly label. Never an identity key.</summary>
    public CustomerDisplayName? DisplayName { get; private set; }

    /// <summary>
    /// The customer this one was merged into, if it was.
    /// </summary>
    /// <remarks>
    /// <strong>At most one hop, always.</strong> Only an unmerged customer may be
    /// merged, and only into an unmerged customer, so this forms a forest of depth
    /// one rather than a chain to be walked or a cycle to be detected. That rule
    /// is what makes <c>A -&gt; B</c> and <c>B -&gt; A</c> impossible rather than
    /// merely unlikely: the moment <c>A</c> points at <c>B</c> it is no longer a
    /// legal target, so the opposite merge has nothing to attach to.
    /// </remarks>
    public CustomerId? MergedIntoCustomerId { get; private set; }

    /// <summary>When the merge happened.</summary>
    public DateTimeOffset? MergedAt { get; private set; }

    /// <summary>
    /// Who performed the merge.
    /// </summary>
    /// <remarks>
    /// Recorded because a merge combines two people's histories and cannot be
    /// undone in this phase, so "who decided this" is the first question anybody
    /// will ask afterwards.
    /// </remarks>
    public UserId? MergedBy { get; private set; }

    /// <summary>When the customer was first seen.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the customer was last changed by an administrator.</summary>
    /// <remarks>
    /// Not "last seen". Conversation activity is recorded on conversations, and
    /// duplicating it here would create a second high-water mark to keep in step.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this record has been merged into another.</summary>
    public bool IsMerged => MergedIntoCustomerId is not null;

    /// <summary>Whether this record is the canonical one for its person.</summary>
    public bool IsCanonical => !IsMerged;

    /// <summary>Records a newly seen customer.</summary>
    /// <remarks>
    /// Called from the transport boundary that already knows a real relationship
    /// exists — a first customer turn on the web, a first message on WhatsApp —
    /// and never from a page view.
    /// </remarks>
    public static Customer Create(
        TenantId tenantId,
        CustomerDisplayName? displayName,
        DateTimeOffset createdAt) =>
        new(
            CustomerId.New(),
            RequireTenant(tenantId),
            displayName,
            mergedIntoCustomerId: null,
            mergedAt: null,
            mergedBy: null,
            RequireInstant(createdAt),
            createdAt);

    /// <summary>Rehydrates a stored customer. Persistence only.</summary>
    public static Customer FromStored(
        CustomerId id,
        TenantId tenantId,
        CustomerDisplayName? displayName,
        CustomerId? mergedIntoCustomerId,
        DateTimeOffset? mergedAt,
        UserId? mergedBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(
            id,
            tenantId,
            displayName,
            mergedIntoCustomerId,
            mergedAt,
            mergedBy,
            createdAt,
            updatedAt);

    /// <summary>
    /// Sets or clears the label.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the label is already exactly this, so a caller
    /// can avoid writing a row and advancing a version for no change.
    /// </returns>
    /// <exception cref="CustomerAlreadyMergedException">
    /// The record has been merged; the canonical customer is the one to edit.
    /// </exception>
    public bool Rename(CustomerDisplayName? displayName, DateTimeOffset renamedAt)
    {
        RequireCanonical();

        if (DisplayName?.Value == displayName?.Value)
        {
            return false;
        }

        DisplayName = displayName;
        UpdatedAt = RequireInstant(renamedAt);

        return true;
    }

    /// <summary>
    /// Points this record at the customer it has been merged into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The aggregate refuses every case that would corrupt the graph: merging
    /// into itself, merging a record that is already merged, merging into a record
    /// that is already merged, and merging across workspaces. It is given the
    /// whole target rather than its id precisely so it can make the third of those
    /// checks itself; the caller's job is to have both rows locked while it does.
    /// </para>
    /// <para>
    /// Moving the channel identities and the conversations is the caller's work,
    /// in the same transaction. Doing it here would mean loading every
    /// conversation a person ever had in order to combine two records.
    /// </para>
    /// </remarks>
    /// <exception cref="CustomerMergeNotAllowedException">
    /// Source and target are the same record, or belong to different workspaces.
    /// </exception>
    /// <exception cref="CustomerAlreadyMergedException">
    /// This record has already been merged into some customer.
    /// </exception>
    public void MergeInto(Customer target, UserId mergedBy, DateTimeOffset mergedAt)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Id == Id)
        {
            throw new CustomerMergeNotAllowedException(
                "A customer cannot be merged into itself.");
        }

        if (target.TenantId != TenantId)
        {
            // Not a validation nicety. Two workspaces' customers are two
            // populations, and a merge across them would move one workspace's
            // conversations under a record the other workspace can read.
            throw new CustomerMergeNotAllowedException(
                "A customer can only be merged into a customer in the same workspace.");
        }

        if (IsMerged)
        {
            throw new CustomerAlreadyMergedException(Id, MergedIntoCustomerId!.Value);
        }

        if (target.IsMerged)
        {
            // Allowing it would build a chain, and a chain is one refactor away
            // from a cycle. The caller is told which record is canonical so it can
            // ask again with the right target.
            throw new CustomerAlreadyMergedException(
                target.Id,
                target.MergedIntoCustomerId!.Value);
        }

        MergedIntoCustomerId = target.Id;
        MergedAt = RequireInstant(mergedAt);
        MergedBy = mergedBy;
        UpdatedAt = mergedAt;
    }

    private void RequireCanonical()
    {
        if (IsMerged)
        {
            throw new CustomerAlreadyMergedException(Id, MergedIntoCustomerId!.Value);
        }
    }

    private static TenantId RequireTenant(TenantId tenantId) =>
        tenantId.IsEmpty
            ? throw new DomainValidationException("A customer must belong to a workspace.")
            : tenantId;

    private static DateTimeOffset RequireInstant(DateTimeOffset instant) =>
        instant == default
            ? throw new DomainValidationException("A customer timestamp is required.")
            : instant;
}

/// <summary>
/// Raised when an operation needs a canonical customer and was given a merged
/// one.
/// </summary>
/// <remarks>
/// It carries the canonical id rather than only refusing, because the caller's
/// next move is always to retry against that record, and making them go and look
/// it up is how a stale link becomes a dead end.
/// </remarks>
public sealed class CustomerAlreadyMergedException(CustomerId customerId, CustomerId canonicalId)
    : Exception($"Customer {customerId} has been merged into {canonicalId}.")
{
    /// <summary>The record that turned out not to be canonical.</summary>
    public CustomerId CustomerId { get; } = customerId;

    /// <summary>The record that is.</summary>
    public CustomerId CanonicalId { get; } = canonicalId;
}

/// <summary>Raised when a merge is refused on its own terms.</summary>
public sealed class CustomerMergeNotAllowedException(string message) : Exception(message);
