namespace OmniAI.Domain.Invitations;

/// <summary>
/// What has become of an invitation.
/// </summary>
/// <remarks>
/// <strong>Expiry is decided by the clock and recorded lazily.</strong> Whether
/// an invitation may still be accepted is always computed from
/// <c>ExpiresAt</c> against the current instant -- so an invitation whose row
/// still says <see cref="Pending"/> is refused the moment it is too old, with no
/// sweeper process to fall behind and nothing to go stale. <see cref="Expired"/>
/// is written only when the platform next has reason to touch that invitation,
/// which in this phase means when a replacement is issued for the same address.
/// <para>
/// <strong>Why record it at all, rather than deriving it everywhere.</strong>
/// Only one open invitation may exist per workspace and address, and that rule is
/// a unique index. A partial index cannot be predicated on <c>now()</c> -- the
/// expression is volatile, so PostgreSQL will not build one, and a rule that
/// changed as the clock moved would not be a constraint. So the index is
/// predicated on the stored status, and an invitation that has aged out is
/// stamped <see cref="Expired"/> as part of issuing its replacement. An expired
/// invitation therefore never permanently blocks a new one, and the constraint
/// stays a constraint.
/// </para>
/// <para>
/// <strong>Nothing here describes delivery.</strong> There is no Sent, Delivered,
/// Opened or Bounced, because this platform sends nothing: an administrator
/// copies a link and passes it on however they already talk to the person. States
/// that no code can ever set are worse than absent -- they invite a reader to
/// believe the platform knows something it does not.
/// </para>
/// <para>
/// Both terminal states are final. There is no reopening: an invitation that was
/// revoked stays revoked and a replacement is issued instead, so the link that
/// was shared can never start working again.
/// </para>
/// </remarks>
public enum TenantInvitationStatus
{
    /// <summary>Issued, not yet used, not withdrawn. Usable until it expires.</summary>
    Pending = 0,

    /// <summary>The invited account accepted it. Terminal, and single-use.</summary>
    Accepted = 1,

    /// <summary>An administrator withdrew it. Terminal.</summary>
    Revoked = 2,

    /// <summary>
    /// It aged out before anybody used it. Terminal.
    /// </summary>
    /// <remarks>
    /// Recorded rather than swept: an invitation past its expiry is already
    /// unusable whatever this column says, and this is stamped when a replacement
    /// is issued so the open-invitation index has room for it.
    /// </remarks>
    Expired = 3,
}
