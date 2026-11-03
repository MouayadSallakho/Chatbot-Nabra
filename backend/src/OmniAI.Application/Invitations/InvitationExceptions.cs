namespace OmniAI.Application.Invitations;

/// <summary>
/// A token was recognised, but the invitation it names can no longer be used.
/// </summary>
/// <remarks>
/// <strong>Deliberately distinct from "no such invitation".</strong> Somebody
/// holding a link that has expired or been withdrawn is not making a mistake and
/// is not being probed for -- they have a genuine invitation that has run out of
/// time or been closed. Telling them that plainly lets them ask for a new one,
/// where a blank not-found would have them checking the link for typos.
/// <para>
/// It says which of the three endings occurred, because "it expired" and "it was
/// withdrawn" lead a person to do different things, and neither discloses
/// anything they did not already have: the token in their hand is the only
/// evidence needed to learn any of it.
/// </para>
/// </remarks>
public sealed class InvitationNoLongerUsableException : Exception
{
    public InvitationNoLongerUsableException()
        : base("This invitation can no longer be used.")
    {
    }

    public InvitationNoLongerUsableException(string message)
        : base(message)
    {
    }

    public InvitationNoLongerUsableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>It was already used.</summary>
    public static InvitationNoLongerUsableException AlreadyAccepted() =>
        new("This invitation has already been used. Ask for a new one if you still need access.");

    /// <summary>It was withdrawn.</summary>
    public static InvitationNoLongerUsableException Revoked() =>
        new("This invitation was withdrawn. Ask whoever invited you for a new one.");

    /// <summary>It ran out of time.</summary>
    public static InvitationNoLongerUsableException Expired() =>
        new("This invitation has expired. Ask whoever invited you for a new one.");

    /// <summary>
    /// It is intact, but the authority behind it no longer stands.
    /// </summary>
    /// <remarks>
    /// The inviter has lost the standing to have issued it, or the roles it
    /// carries have outgrown what they could delegate. Reported to the invitee
    /// without those details: it is not their situation to fix and none of it is
    /// theirs to know, so they are told what to do rather than why.
    /// </remarks>
    public static InvitationNoLongerUsableException NoLongerValid() =>
        new("This invitation is no longer valid. Ask whoever invited you for a new one.");
}

/// <summary>
/// The signed-in account is not the account that was invited.
/// </summary>
/// <remarks>
/// <strong>The second half of what makes an invitation safe.</strong> The token
/// proves somebody was given the link; this proves they are the person it was
/// written to. Without it a forwarded link, a shared screen or a mail archive
/// would be enough for anybody to join.
/// <para>
/// The message names neither address. The person reading it already knows which
/// account they are signed in as, and telling them who the invitation was for
/// would turn a leaked link into a way to learn a colleague's email.
/// </para>
/// </remarks>
public sealed class InvitationNotForThisAccountException : Exception
{
    private const string DefaultMessage =
        "This invitation was sent to a different account. Sign in with the invited address to "
        + "accept it.";

    public InvitationNotForThisAccountException()
        : base(DefaultMessage)
    {
    }

    public InvitationNotForThisAccountException(string message)
        : base(message)
    {
    }

    public InvitationNotForThisAccountException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The workspace already has an open invitation for this address.
/// </summary>
/// <remarks>
/// One open invitation per address, so there is never a question of which of two
/// links is the real one, and revoking does not leave a second still working.
/// </remarks>
public sealed class InvitationAlreadyOpenException : Exception
{
    private const string DefaultMessage =
        "This workspace already has an open invitation for that address. Revoke it before "
        + "creating another.";

    public InvitationAlreadyOpenException()
        : base(DefaultMessage)
    {
    }

    public InvitationAlreadyOpenException(string message)
        : base(message)
    {
    }

    public InvitationAlreadyOpenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The address invited already belongs to a member of this workspace.
/// </summary>
/// <remarks>
/// Reported rather than silently succeeding, because an invitation that could
/// never usefully be accepted is a link somebody will send and wonder about. To
/// change what an existing member can do, their roles are edited directly.
/// </remarks>
public sealed class AlreadyAMemberException : Exception
{
    private const string DefaultMessage =
        "That person is already a member of this workspace. Change their roles instead of "
        + "inviting them again.";

    public AlreadyAMemberException()
        : base(DefaultMessage)
    {
    }

    public AlreadyAMemberException(string message)
        : base(message)
    {
    }

    public AlreadyAMemberException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The invitation list was asked for a status it does not know (Phase 37.5b, row 24).
/// </summary>
/// <remarks>
/// Refused rather than ignored, so a misspelt filter never shows every invitation
/// under a heading that promises only the pending ones. Raised before membership is
/// checked, so the answer is the same for anybody and says nothing about the
/// workspace. Published as <c>400 invitations.status.invalid</c>.
/// </remarks>
public sealed class InvitationStatusFilterInvalidException : Exception
{
    private const string DefaultMessage =
        "That status is not known. Use Pending, Accepted, Revoked or Expired, or leave the "
        + "status out.";

    public InvitationStatusFilterInvalidException()
        : base(DefaultMessage)
    {
    }

    public InvitationStatusFilterInvalidException(string message)
        : base(message)
    {
    }

    public InvitationStatusFilterInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
