using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Application.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Invitations.ListTenantInvitations;

/// <summary>One page of a workspace's invitations.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">How many to return.</param>
public sealed record ListTenantInvitationsQuery(Guid TenantId, int Page = 1, int PageSize = 20);

/// <summary>
/// Lists invitations.
/// </summary>
/// <remarks>
/// Requires <c>members.view</c>, the same permission as the member list: an open
/// invitation is a statement about who is about to be on the team, and somebody
/// entitled to see the team is entitled to see that.
/// </remarks>
public sealed class ListTenantInvitationsHandler(
    ITenantInvitationRepository invitations,
    ITenantRoleRepository roles,
    IAccountDirectory accounts,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    /// <summary>The largest page this endpoint will produce.</summary>
    public const int MaxPageSize = 100;

    /// <summary>The page size used when none is asked for.</summary>
    public const int DefaultPageSize = 20;

    private readonly ITenantInvitationRepository _invitations =
        invitations ?? throw new ArgumentNullException(nameof(invitations));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly IAccountDirectory _accounts =
        accounts ?? throw new ArgumentNullException(nameof(accounts));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Reads a page.</summary>
    /// <returns>
    /// <see langword="null"/> when the caller does not belong to this workspace.
    /// </returns>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>members.view</c>.
    /// </exception>
    public async Task<TenantInvitationPage?> HandleAsync(
        ListTenantInvitationsQuery query,
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

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaxPageSize);

        int total = await _invitations
            .CountAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TenantInvitation> stored = await _invitations
            .ListAsync(tenantId, (page - 1) * pageSize, pageSize, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();
        List<TenantInvitationView> items = [];

        foreach (TenantInvitation invitation in stored)
        {
            items.Add(new TenantInvitationView(
                invitation.Id.Value,
                invitation.Email.Value,
                TenantInvitations.StatusAt(invitation, now),
                await RoleNamesAsync(tenantId, invitation, cancellationToken).ConfigureAwait(false),
                await InviterEmailAsync(invitation, cancellationToken).ConfigureAwait(false),
                invitation.CreatedAt,
                invitation.ExpiresAt));
        }

        return new TenantInvitationPage(items, page, pageSize, total);
    }

    /// <summary>
    /// The names of the roles an invitation would grant.
    /// </summary>
    /// <remarks>
    /// Read from the roles as they stand, so an invitation naming a role that has
    /// since been renamed shows the current name rather than a stale copy nobody
    /// would recognise. A role that has gone is simply absent -- acceptance will
    /// refuse the invitation anyway, and inventing a placeholder name would
    /// suggest it still grants something.
    /// </remarks>
    private async Task<IReadOnlyList<string>> RoleNamesAsync(
        TenantId tenantId,
        TenantInvitation invitation,
        CancellationToken cancellationToken)
    {
        List<string> names = [];

        foreach (TenantRoleId roleId in invitation.RoleIds)
        {
            Versioned<TenantRole>? role = await _roles
                .FindAsync(tenantId, roleId, cancellationToken)
                .ConfigureAwait(false);

            if (role is not null)
            {
                names.Add(role.Value.Name.Value);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        return names;
    }

    /// <summary>
    /// Who issued an invitation, by address.
    /// </summary>
    /// <remarks>
    /// Safe to show to somebody who may already see every member's address. Null
    /// when the account is gone, rather than a placeholder that would look like a
    /// person.
    /// </remarks>
    private async Task<string?> InviterEmailAsync(
        TenantInvitation invitation,
        CancellationToken cancellationToken)
    {
        EmailAddress? email = await _accounts
            .FindEmailAsync(invitation.InvitedByUserId, cancellationToken)
            .ConfigureAwait(false);

        return email?.Value;
    }
}
