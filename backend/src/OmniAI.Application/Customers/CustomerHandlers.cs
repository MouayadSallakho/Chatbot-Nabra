using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Customers.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Customers;

/// <summary>
/// Reading and administering a workspace's customer directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two permissions, and a third borrowed.</strong> Reading the directory
/// needs <c>customers.view</c>; changing anything needs <c>customers.manage</c>;
/// and seeing a customer's conversations needs <c>conversations.view</c> as well,
/// because a transcript is not customer data and this screen must not become a
/// way around the permission that guards it.
/// </para>
/// <para>
/// <strong>Nothing here resolves identity.</strong> There is no search by phone,
/// no match by name, no suggestion of who might be the same person. The only way
/// two customers become one is an administrator naming both and confirming it.
/// </para>
/// </remarks>
public sealed class CustomerHandlers(
    TenantAuthorizer authorizer,
    ICustomerRepository customers,
    TimeProvider clock)
{
    /// <summary>
    /// How many of a customer's conversations the detail page carries.
    /// </summary>
    /// <remarks>
    /// Bounded because a long-standing customer can have hundreds and the page
    /// shows a history, not an archive. The inbox is where somebody goes to search
    /// conversations, and it has its own paging.
    /// </remarks>
    public const int MaxConversationHistory = 50;

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ICustomerRepository _customers =
        customers ?? throw new ArgumentNullException(nameof(customers));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>The page, or <see langword="null"/> when the caller may not read it.</returns>
    /// <exception cref="CustomerPageValidationException">
    /// The page number or size is outside what the directory serves.
    /// </exception>
    public async Task<CustomerPage?> ListAsync(
        ListCustomersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await ViewAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int page = query.Page ?? 1;
        int pageSize = query.PageSize ?? CustomerPage.DefaultPageSize;

        if (page < 1)
        {
            throw new CustomerPageValidationException(
                $"Page must be 1 or greater, but was {page}.");
        }

        if (pageSize < 1 || pageSize > CustomerPage.MaxPageSize)
        {
            throw new CustomerPageValidationException(
                $"Page size must be between 1 and {CustomerPage.MaxPageSize}, but was {pageSize}.");
        }

        (IReadOnlyList<CustomerDirectoryRow> rows, int total) = await _customers
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return new CustomerPage(
            [.. rows.Select(row => new CustomerRow(
                row.Id.Value,
                row.DisplayName?.Value,
                CustomerLabels.For(row.DisplayName, row.Channels),
                row.Channels,
                row.ConversationCount,
                row.LastActivityAt,
                row.CreatedAt,
                CustomerLabels.CodeFor(row.DisplayName, row.Channels),
                CustomerShortCode.For(row.Id)))],
            page,
            pageSize,
            total);
    }

    /// <returns>The customer, or <see langword="null"/>.</returns>
    public async Task<Versioned<CustomerView>?> GetAsync(
        GetCustomerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await ViewAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        CustomerId customerId = CustomerId.From(query.CustomerId);

        Versioned<Customer>? found = await _customers
            .FindAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        // A merged record is readable on purpose. Somebody following a link from
        // an older screen must be told where their customer went rather than
        // meeting a 404 that looks like data loss.
        return await ProjectAsync(tenantId, found, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the full view of a customer the caller has already been authorised
    /// to act on.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetAsync"/> so that a caller who may manage
    /// customers but may not read the directory still gets an answer back from a
    /// write. Managing does not require viewing anywhere else on this platform,
    /// and re-entering the read path would have quietly made it require it here.
    /// </remarks>
    private async Task<Versioned<CustomerView>> ProjectAsync(
        TenantId tenantId,
        Versioned<Customer> found,
        CancellationToken cancellationToken)
    {
        Customer customer = found.Value;

        CustomerId customerId = customer.Id;

        CustomerDirectoryRow? summary = await _customers
            .SummariseAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<CustomerChannelIdentityView> identities = await _customers
            .ListIdentitiesAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        // The second permission, asked separately and never implied by the first.
        //
        // Asked as a question rather than as a requirement: somebody who may
        // administer customers but may not read transcripts is a perfectly
        // legitimate reader of this page, and refusing their whole request over
        // the section they cannot see would make the permission useless on its
        // own.
        bool canReadConversations = await _authorizer
            .AuthorizeAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false) == TenantAccessDecision.Granted;

        IReadOnlyList<CustomerConversationView> conversations = canReadConversations
            ? await _customers
                .ListConversationsAsync(
                    tenantId,
                    customerId,
                    MaxConversationHistory,
                    cancellationToken)
                .ConfigureAwait(false)
            : [];

        IReadOnlyList<ChannelKind> channels = summary?.Channels ?? [];

        return new Versioned<CustomerView>(
            new CustomerView(
                customer.Id.Value,
                customer.DisplayName?.Value,
                CustomerLabels.For(customer.DisplayName, channels),
                identities,
                channels,
                summary?.ConversationCount ?? 0,
                summary?.LastActivityAt,
                customer.CreatedAt,
                customer.UpdatedAt,
                customer.MergedIntoCustomerId?.Value,
                customer.MergedAt,
                conversations,
                canReadConversations,
                CustomerLabels.CodeFor(customer.DisplayName, channels),
                CustomerShortCode.For(customer.Id)),
            found.Version);
    }

    /// <returns>The changed customer, or <see langword="null"/>.</returns>
    /// <exception cref="CustomerVersionMismatchException">
    /// Somebody else changed it in between.
    /// </exception>
    /// <exception cref="CustomerAlreadyMergedException">
    /// The record is a tombstone; the canonical customer is the one to edit.
    /// </exception>
    public async Task<Versioned<CustomerView>?> RenameAsync(
        RenameCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        if (!await ManageAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        CustomerId customerId = CustomerId.From(command.CustomerId);

        Versioned<Customer>? found = await _customers
            .FindAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        // Checked before the write, so a caller holding a stale version is told so
        // rather than overwriting somebody's edit. The repository checks it again
        // against the row, because the two reads are not in the same transaction.
        if (found.Version != command.Expected)
        {
            throw new CustomerVersionMismatchException();
        }

        Customer customer = found.Value;

        // Throws for a tombstone. Renaming one would put a label where nobody
        // looks and leave the canonical record unchanged.
        if (customer.Rename(
            CustomerDisplayName.CreateOrNull(command.DisplayName),
            _clock.GetUtcNow()))
        {
            await _customers
                .UpdateAsync(customer, command.Expected, cancellationToken)
                .ConfigureAwait(false);
        }

        // Re-read, so the version returned is the one the write produced and the
        // aggregated facts are current.
        Versioned<Customer>? saved = await _customers
            .FindAsync(tenantId, customerId, cancellationToken)
            .ConfigureAwait(false);

        return saved is null
            ? null
            : await ProjectAsync(tenantId, saved, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>What the merge did, or <see langword="null"/> when refused.</returns>
    /// <exception cref="CustomerMergeNotAllowedException">
    /// Same record, or a target that is itself merged.
    /// </exception>
    /// <exception cref="CustomerAlreadyMergedException">
    /// The source is already merged into a different customer.
    /// </exception>
    /// <exception cref="CustomerVersionMismatchException">
    /// The source changed since the caller read it.
    /// </exception>
    public async Task<CustomerMergeResult?> MergeAsync(
        MergeCustomersCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        if (!await ManageAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        CustomerId sourceId = CustomerId.From(command.SourceCustomerId);
        CustomerId targetId = CustomerId.From(command.TargetCustomerId);

        if (sourceId == targetId)
        {
            throw CustomerMergeNotAllowedException.Self();
        }

        // Both must exist in this workspace before anything is locked. A merge
        // naming a customer of another workspace is answered as "not found",
        // exactly as reading that customer would be: a merge form is not a way to
        // discover which ids exist elsewhere.
        if (await _customers.FindAsync(tenantId, sourceId, cancellationToken).ConfigureAwait(false)
            is null
            || await _customers.FindAsync(tenantId, targetId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return null;
        }

        UserId mergedBy = _authorizer.RequireCurrentUser();

        (CustomerMergeCounts counts, bool alreadyMerged) = await _customers
            .MergeAsync(
                tenantId,
                sourceId,
                targetId,
                mergedBy,
                command.Expected,
                _clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        return new CustomerMergeResult(
            sourceId.Value,
            targetId.Value,
            counts.Conversations,
            counts.Identities,
            alreadyMerged);
    }

    private Task<bool> ViewAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(tenantId, PlatformPermissions.CustomersView, cancellationToken);

    /// <summary>
    /// The permission every write here needs, and the only one.
    /// </summary>
    /// <remarks>
    /// Managing does not require viewing, in keeping with the rest of the
    /// platform: no permission implies another, so what somebody may do is
    /// readable from their roles without evaluating a graph.
    /// </remarks>
    private Task<bool> ManageAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        _authorizer.RequireAsync(tenantId, PlatformPermissions.CustomersManage, cancellationToken);
}
