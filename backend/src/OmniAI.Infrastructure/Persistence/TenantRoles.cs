using OmniAI.Domain.Authorization;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// The one piece of role persistence that the model cannot express on its own.
/// </summary>
/// <remarks>
/// A role's name occupies two columns: the display form a person reads, and the
/// normalised form the tenant-scoped unique index is built on. A value converter
/// fills one column, so the second is a shadow property and something has to set
/// it.
/// <para>
/// <strong>It lives here, in one method, rather than in each repository that
/// writes a role.</strong> Two call sites write roles -- workspace provisioning
/// creates the Owner, and the role repository creates and renames custom ones --
/// and a copy in each is the arrangement where one of them is eventually edited
/// and the other is not. A role written with a stale or missing normalised name
/// would either escape the uniqueness rule or collide with a role that does not
/// look like it.
/// </para>
/// <para>
/// <strong>Why not a database-generated column.</strong>
/// <c>GENERATED ALWAYS AS (upper(name)) STORED</c> would need no application code
/// at all, and was rejected: <c>upper()</c> is collation-dependent, so for a
/// non-ASCII role name PostgreSQL's idea of the normalised form and
/// <c>ToUpperInvariant</c>'s could differ. This platform would then have two
/// definitions of when two role names are the same, and the pre-check and the
/// index would disagree in exactly the cases nobody tests. One definition, in the
/// Domain, is worth one assignment here.
/// </para>
/// </remarks>
internal static class TenantRoles
{
    /// <summary>
    /// Copies the role's normalised name onto the tracked entry.
    /// </summary>
    /// <remarks>
    /// Called after the role is added or after it is renamed. Reading it back is
    /// never necessary: the display name is the source, and the normalised form is
    /// derived from it every time it is written.
    /// </remarks>
    public static void SetNormalisedName(OmniAiDbContext context, TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(role);

        context.Entry(role)
            .Property<string>(PersistenceSchema.NormalisedRoleName)
            .CurrentValue = role.Name.Normalised;
    }
}
