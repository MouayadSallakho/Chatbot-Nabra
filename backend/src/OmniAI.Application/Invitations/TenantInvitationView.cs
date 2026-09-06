using OmniAI.Domain.Invitations;

namespace OmniAI.Application.Invitations;

/// <summary>
/// One invitation, as the Team screen is told about it.
/// </summary>
/// <param name="Id">The invitation's identity. Not its secret.</param>
/// <param name="Email">The address invited.</param>
/// <param name="Status">Where it stands, with expiry already applied.</param>
/// <param name="Roles">The roles it would grant, by name.</param>
/// <param name="InvitedByEmail">Who issued it, or null if that account is gone.</param>
/// <param name="CreatedAt">When it was issued.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <remarks>
/// <strong>Neither the token nor its digest appears here, and there is no field
/// they could occupy.</strong> The secret is returned once, by the request that
/// created it, and this record is what every later read produces instead. A
/// listing that could show the token would make every administrator able to
/// accept every outstanding invitation.
/// <para>
/// <strong>The status is resolved, not copied.</strong> A row still recorded as
/// pending is reported as expired once the clock has passed its expiry, because
/// that is what it is -- showing an administrator a pending invitation that
/// nobody can accept would have them waiting for something that cannot happen.
/// </para>
/// </remarks>
public sealed record TenantInvitationView(
    Guid Id,
    string Email,
    TenantInvitationStatus Status,
    IReadOnlyList<string> Roles,
    string? InvitedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>A page of invitations.</summary>
/// <param name="Items">The invitations on this page.</param>
/// <param name="Page">The page returned.</param>
/// <param name="PageSize">The size applied after clamping.</param>
/// <param name="Total">How many the workspace has, in any state.</param>
public sealed record TenantInvitationPage(
    IReadOnlyList<TenantInvitationView> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Resolving an invitation for display.</summary>
internal static class TenantInvitations
{
    /// <summary>
    /// The status to show, with the clock taken into account.
    /// </summary>
    /// <remarks>
    /// The one place expiry is applied to a stored status, so the list, the
    /// detail and any future screen cannot disagree about whether an invitation
    /// is still open.
    /// </remarks>
    public static TenantInvitationStatus StatusAt(TenantInvitation invitation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        return invitation.HasExpiredAt(now)
            ? TenantInvitationStatus.Expired
            : invitation.Status;
    }
}
