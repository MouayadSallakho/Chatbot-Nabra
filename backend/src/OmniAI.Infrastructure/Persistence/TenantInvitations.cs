using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Invitations;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Keeping an invitation's stored comparison form in step with its address.
/// </summary>
/// <remarks>
/// The canonical email is a shadow property: the Domain already derives it, and a
/// second property the Domain also knew about would be two places that could
/// disagree about what "the same address" means. It is set here, on write, from
/// the value object -- the same arrangement the normalised role name uses.
/// <para>
/// A database-computed column was considered and rejected for the same reason it
/// was for role names: <c>upper()</c> is collation-dependent, so the rule deciding
/// who somebody is would become a property of the database's locale rather than
/// of this platform.
/// </para>
/// </remarks>
internal static class TenantInvitations
{
    /// <summary>Stamps the canonical address onto a tracked invitation.</summary>
    public static void SetCanonicalEmail(OmniAiDbContext context, TenantInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invitation);

        context.Entry(invitation)
            .Property<string>(PersistenceSchema.CanonicalEmail)
            .CurrentValue = invitation.Email.Canonical;
    }
}
