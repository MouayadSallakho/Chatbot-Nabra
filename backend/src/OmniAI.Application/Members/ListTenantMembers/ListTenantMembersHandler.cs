using OmniAI.Application.Authorization;
using OmniAI.Application.Members.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Members.ListTenantMembers;

/// <summary>One page of a workspace's members.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">How many members to return.</param>
public sealed record ListTenantMembersQuery(Guid TenantId, int Page = 1, int PageSize = 20);

/// <summary>A page of members.</summary>
/// <param name="Items">The members on this page.</param>
/// <param name="Page">The page returned.</param>
/// <param name="PageSize">The size applied after clamping.</param>
/// <param name="Total">How many members the workspace has.</param>
public sealed record TenantMemberPage(
    IReadOnlyList<TenantMemberSummary> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// Lists who belongs to a workspace.
/// </summary>
/// <remarks>
/// Bounded and ordered for the same reasons the agent list is: an unbounded read
/// is a denial-of-service waiting for the first large workspace, and an unstable
/// order makes paging show a row twice or skip it entirely.
/// </remarks>
public sealed class ListTenantMembersHandler(
    ITenantMemberRepository members,
    TenantAuthorizer authorizer)
{
    /// <summary>The largest page this endpoint will produce.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The page size used when none is asked for.</summary>
    public const int DefaultPageSize = 20;

    private readonly ITenantMemberRepository _members =
        members ?? throw new ArgumentNullException(nameof(members));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Reads a page.</summary>
    /// <returns>
    /// <see langword="null"/> when the caller does not belong to this workspace.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>members.view</c>.
    /// </exception>
    public async Task<TenantMemberPage?> HandleAsync(
        ListTenantMembersQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        // Clamped rather than rejected: a page size of zero or a negative page is
        // a client defect, and answering it with the first page is more useful
        // than a validation error nobody can act on.
        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaxPageSize);

        int total = await _members.CountAsync(tenantId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TenantMemberRecord> records = await _members
            .ListAsync(tenantId, (page - 1) * pageSize, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return new TenantMemberPage(
            [.. records.Select(TenantMembers.Summarise)],
            page,
            pageSize,
            total);
    }
}
