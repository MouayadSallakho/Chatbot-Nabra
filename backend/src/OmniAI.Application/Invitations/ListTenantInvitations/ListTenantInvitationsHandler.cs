using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
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
/// <param name="Status">
/// Only invitations in this status, by its wire name: <c>Pending</c>,
/// <c>Accepted</c>, <c>Revoked</c> or <c>Expired</c> (Phase 37.5b, row 24).
/// <see langword="null"/> or empty lists every invitation, as before.
/// </param>
public sealed record ListTenantInvitationsQuery(
    Guid TenantId,
    int Page = 1,
    int PageSize = 20,
    string? Status = null);

/// <summary>
/// Lists invitations.
/// </summary>
/// <remarks>
/// Requires <c>members.view</c>, the same permission as the member list: an open
/// invitation is a statement about who is about to be on the team, and somebody
/// entitled to see the team is entitled to see that.
/// <para>
/// <strong>A constant number of statements per page (Phase 37.5b).</strong> The
/// count, the page, the role names of every invitation on it and the inviters'
/// addresses are read once each, however many invitations the page holds. Before,
/// each invitation read each of its roles and its inviter separately.
/// </para>
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
    /// <exception cref="InvitationStatusFilterInvalidException">
    /// <see cref="ListTenantInvitationsQuery.Status"/> names no status. Checked
    /// first, so the answer is the same for anybody.
    /// </exception>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>members.view</c>.
    /// </exception>
    public async Task<TenantInvitationPage?> HandleAsync(
        ListTenantInvitationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantInvitationStatus? status = ParseStatus(query.Status);
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

        // One instant for the filter and for every status shown, so a row the
        // filter called pending is never shown as expired on the same page.
        DateTimeOffset now = _clock.GetUtcNow();

        int total = await _invitations
            .CountAsync(tenantId, status, now, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<TenantInvitation> stored = await _invitations
            .ListAsync(tenantId, status, now, (page - 1) * pageSize, pageSize, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<TenantRoleId, string> roleNames = await _roles
            .FindNamesAsync(tenantId, [.. stored.SelectMany(invitation => invitation.RoleIds)], cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<UserId, EmailAddress> inviters = await _accounts
            .FindEmailsAsync([.. stored.Select(invitation => invitation.InvitedByUserId)], cancellationToken)
            .ConfigureAwait(false);

        List<TenantInvitationView> items = [];

        foreach (TenantInvitation invitation in stored)
        {
            items.Add(new TenantInvitationView(
                invitation.Id.Value,
                invitation.Email.Value,
                TenantInvitations.StatusAt(invitation, now),
                RoleNames(invitation, roleNames),
                inviters.TryGetValue(invitation.InvitedByUserId, out EmailAddress inviter) ? inviter.Value : null,
                invitation.CreatedAt,
                invitation.ExpiresAt));
        }

        return new TenantInvitationPage(items, page, pageSize, total);
    }

    /// <summary>
    /// The status a filter names, or <see langword="null"/> for no filter.
    /// </summary>
    /// <remarks>
    /// The wire names, in any letter case. A number is refused even though the enum
    /// would parse one: the contract is the name, and <c>?status=0</c> working by
    /// accident would become something a client relies on.
    /// </remarks>
    private static TenantInvitationStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.All(char.IsAsciiLetter)
            && Enum.TryParse(trimmed, ignoreCase: true, out TenantInvitationStatus status)
            && Enum.IsDefined(status)
                ? status
                : throw new InvitationStatusFilterInvalidException();
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
    private static List<string> RoleNames(
        TenantInvitation invitation,
        IReadOnlyDictionary<TenantRoleId, string> roleNames)
    {
        List<string> names =
        [
            .. invitation.RoleIds
                .Where(roleNames.ContainsKey)
                .Select(roleId => roleNames[roleId]),
        ];

        names.Sort(StringComparer.OrdinalIgnoreCase);

        return names;
    }
}
