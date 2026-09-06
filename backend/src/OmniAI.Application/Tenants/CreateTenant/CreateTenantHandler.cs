using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tenants.CreateTenant;

/// <summary>
/// Creates a workspace for the signed-in person.
/// </summary>
/// <remarks>
/// A plain class with constructor dependencies, resolved by name at the
/// composition root. There is no mediator, command bus or pipeline: a dispatcher
/// would add indirection, reflection and a registration convention while removing
/// nothing -- and it would hide the call graph that is currently readable in one
/// hop.
/// <para>
/// <strong>The creator becomes the Owner, in the same transaction.</strong>
/// Phase 12's rule is that a tenant id grants nothing on its own; access requires
/// a membership row. Phase 13 added a second rule beside it: a membership grants
/// nothing on its own either, and what may be done inside a workspace comes from
/// the roles the member holds. Together those make creating a tenant without the
/// other three rows not merely untidy but broken. Without the membership the
/// creator cannot reach the workspace at all; without the Owner role there is no
/// full authority in it and no way to make one, because no caller can mint a
/// system role; without the assignment the creator is a member who may do
/// nothing, in a workspace where nobody can grant them anything. None of those
/// states is recoverable through any endpoint this platform publishes, so all
/// four rows are written together or not at all.
/// </para>
/// <para>
/// <strong>The command carries no user id.</strong> Who is creating the workspace
/// is read from <see cref="ICurrentUser"/>, never accepted as input. A
/// <c>UserId</c> parameter would be a field that an endpoint, a test or a future
/// caller could fill in with somebody else's identity, and its correctness would
/// then depend on every call site rather than on this line.
/// </para>
/// </remarks>
public sealed class CreateTenantHandler(
    ITenantRepository tenants,
    TenantAccessGuard access,
    TimeProvider clock)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Creates the tenant and returns its read model.</summary>
    /// <exception cref="NotAuthenticatedException">
    /// Nobody is signed in. Checked first: an anonymous caller must be told to
    /// sign in, not told that their proposed slug is malformed.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">
    /// The name or slug breaks a Domain rule. Raised by the value objects, not
    /// re-implemented here.
    /// </exception>
    /// <exception cref="TenantSlugAlreadyExistsException">The slug is taken.</exception>
    public async Task<TenantView> HandleAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Identity first. Everything below it is work done on behalf of somebody,
        // and there is no somebody yet.
        UserId userId = _access.RequireCurrentUser();

        // The Domain validates and canonicalises. If either value is rejected,
        // this throws before anything touches the database.
        TenantName name = TenantName.Create(command.Name);
        TenantSlug slug = TenantSlug.Create(command.Slug);

        // An early, friendly failure for the overwhelmingly common case: the slug
        // was already taken well before this request arrived. This is a courtesy,
        // not a guard -- the answer can be stale the instant it is read, so the
        // authority is the unique index inside AddAsync. Deliberately no lock, no
        // serialisable transaction and no advisory lock: paying that cost on every
        // onboarding to tidy up a rare race would be the wrong trade, and the
        // database already resolves the race correctly.
        if (await _tenants.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
        {
            throw TenantSlugAlreadyExistsException.ForSlug(slug.Value);
        }

        // The workspace and the three rows that make it usable, built together
        // from one reading of the clock. Two calls to the clock would put
        // different timestamps on what is a single act.
        //
        // Identity and initial status come from the aggregate, and the Owner
        // role's identity, its fixed name and its system kind are equally not the
        // use case's to choose. NewWorkspace is the only way to obtain a
        // consistent set of the four, so this call site cannot pair a membership
        // for one person with an assignment for another.
        NewWorkspace workspace = NewWorkspace.Create(name, slug, userId, _clock.GetUtcNow());

        await _tenants.AddAsync(workspace, cancellationToken).ConfigureAwait(false);

        return TenantView.From(workspace.Tenant);
    }
}
