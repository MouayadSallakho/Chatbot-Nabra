using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.GetMyTenantPermissions;

/// <summary>
/// What the signed-in caller may do in one of their workspaces.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is what the admin application asks so it can stop offering
/// actions that would be refused.</strong> It is a user-experience input and not
/// a security boundary: the browser can be told anything, and every request is
/// authorised again on the server regardless. A client that lied to itself about
/// this endpoint's answer would see buttons it could press and <c>403</c>s when
/// it did.
/// </para>
/// <para>
/// <strong>Membership is enough to ask; no permission is required.</strong>
/// Requiring one would be circular -- a member with nothing granted could not be
/// told they have nothing granted -- and the answer contains only facts about the
/// caller, so there is nothing here to withhold from them.
/// </para>
/// <para>
/// <strong>It reports on nobody else.</strong> The query has no user field and
/// this handler reads the caller from the session, so there is no shape of
/// request that asks about a colleague. Role assignments belong to the
/// member-administration phase, and this endpoint deliberately does not become a
/// preview of it.
/// </para>
/// </remarks>
public sealed class GetMyTenantPermissionsHandler(TenantAuthorizer authorizer)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Reads the caller's effective permissions.</summary>
    /// <returns>
    /// The union of their roles' permissions, deduplicated and in catalogue order,
    /// or <see langword="null"/> when they do not belong to this workspace.
    /// </returns>
    /// <remarks>
    /// An empty list and <see langword="null"/> mean genuinely different things: a
    /// member who has been given no roles yet, and somebody the workspace does not
    /// know. The first is an ordinary state the admin application must explain;
    /// the second is answered exactly as a workspace that never existed.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="Domain.DomainValidationException">The tenant id is empty.</exception>
    public async Task<IReadOnlyList<PermissionCode>?> HandleAsync(
        GetMyTenantPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _authorizer
            .EffectivePermissionsAsync(TenantId.From(query.TenantId), cancellationToken)
            .ConfigureAwait(false);
    }
}
