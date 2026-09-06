using OmniAI.Application.Concurrency;

namespace OmniAI.Application.Members;

/// <summary>
/// A member's roles were changed by somebody else since the caller read them.
/// </summary>
/// <remarks>
/// The membership equivalent of the accepted role and agent version conflicts,
/// and it means the same thing: the operation may be perfectly reasonable, but it
/// was composed against a picture of this person's access that is no longer
/// current. Re-reading is the one thing that fixes it, which is why it is
/// reported as a failed precondition rather than as a conflict -- a conflict
/// would tell somebody to change what they asked for, and here they may well want
/// exactly what they asked for.
/// </remarks>
public sealed class TenantMemberVersionMismatchException : Exception
{
    private const string DefaultMessage =
        "This team member's access was changed by somebody else. Reload to see the current "
        + "assignment before saving.";

    public TenantMemberVersionMismatchException()
        : base(DefaultMessage)
    {
    }

    public TenantMemberVersionMismatchException(string message)
        : base(message)
    {
    }

    public TenantMemberVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The member whose access moved.</summary>
    public Guid? UserId { get; private init; }

    /// <summary>Creates the exception for a known member.</summary>
    public static TenantMemberVersionMismatchException ForMember(Guid userId) =>
        new(DefaultMessage) { UserId = userId };
}

/// <summary>
/// An operation that this platform will not perform on this member, whoever asks.
/// </summary>
/// <remarks>
/// <strong>Distinct from a permission refusal, and the difference matters to the
/// person reading it.</strong> A permission denial says "not you, not yet" and
/// can be resolved by somebody granting you something. This says "not through
/// here, ever" -- an Owner's access is not administered from the Team screen, and
/// nobody edits their own access there. Reporting either as the other would send
/// somebody to ask for a permission that would not help.
/// <para>
/// Both cases are business rules about the target rather than about the caller's
/// authority, which is why an Owner receives them too.
/// </para>
/// </remarks>
public sealed class ProtectedMembershipException : Exception
{
    public ProtectedMembershipException()
        : base("This membership cannot be changed from team management.")
    {
    }

    public ProtectedMembershipException(string message)
        : base(message)
    {
    }

    public ProtectedMembershipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The Owner's access is not assigned from the Team screen.</summary>
    /// <remarks>
    /// Ownership is the right to administer the workspace itself; handing it over
    /// or taking it away is a deliberate act with its own consequences -- there
    /// must always be exactly one, and losing the last one strands the workspace.
    /// Making it a checkbox beside ordinary roles would mean an administrator
    /// could give away the workspace while intending to grant somebody a report.
    /// </remarks>
    public static ProtectedMembershipException Owner() =>
        new("The Owner's access is protected and cannot be changed from team management.");

    /// <summary>Nobody edits their own access here.</summary>
    /// <remarks>
    /// Not paternalism: an administrator who removes their own last role in a
    /// workspace where they are the only administrator has locked everybody out
    /// of role management permanently, and this platform has no recovery path for
    /// that. Refusing the one operation that can cause it costs a real person
    /// almost nothing, because changing your own access is not something anybody
    /// needs to do casually.
    /// </remarks>
    public static ProtectedMembershipException Self() =>
        new("You cannot change your own access from team management.");
}

/// <summary>
/// The membership precondition check.
/// </summary>
/// <remarks>
/// A separate helper for the same reason roles and agents have one: the
/// comparison is two lines, but the decision about <em>when</em> it happens is
/// the contract, and giving it a name means the ordering is visible at every call
/// site rather than implied by where somebody happened to type an <c>if</c>.
/// </remarks>
internal static class TenantMemberConcurrency
{
    /// <summary>
    /// Throws unless the stored membership is still at the expected version.
    /// </summary>
    /// <exception cref="TenantMemberVersionMismatchException">They differ.</exception>
    public static void RequireExpectedVersion(
        Versioned<TenantMemberRecord> stored,
        ConcurrencyToken expected)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Version != expected)
        {
            throw TenantMemberVersionMismatchException.ForMember(stored.Value.UserId.Value);
        }
    }
}
