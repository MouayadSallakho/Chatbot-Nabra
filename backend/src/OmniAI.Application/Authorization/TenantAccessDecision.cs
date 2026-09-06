namespace OmniAI.Application.Authorization;

/// <summary>
/// The outcome of asking whether the current caller may perform one operation in
/// one workspace.
/// </summary>
/// <remarks>
/// <strong>Three outcomes, never two.</strong> "Not a member" and "a member
/// without the permission" are different facts about the world and they produce
/// different answers on the wire -- <c>404</c> and <c>403</c> -- so the decision
/// type keeps them apart all the way from the database to the endpoint. A boolean
/// would force every caller to re-derive the distinction, and the first caller
/// that forgot would either tell a stranger which workspaces exist or tell a
/// member their own workspace had vanished.
/// <para>
/// There is deliberately no fourth member for "not authenticated". That is not a
/// decision about access: nobody has said who they are yet, so there is nothing
/// to decide. It is reported by throwing, exactly as it was before roles existed,
/// because a caller who has not signed in needs a different action from one who
/// has.
/// </para>
/// </remarks>
public enum TenantAccessDecision
{
    /// <summary>
    /// The caller is signed in but has no membership of this workspace -- or the
    /// workspace does not exist. Deliberately the same answer for both.
    /// </summary>
    /// <remarks>
    /// The two are indistinguishable here because the authorizer does not know
    /// which it was either: it asked whether a membership row exists and was told
    /// no. That is what stops a tenant id from being probeable.
    /// </remarks>
    NotAMember = 0,

    /// <summary>
    /// The caller belongs to this workspace, and none of their roles grants the
    /// permission the operation requires.
    /// </summary>
    PermissionDenied = 1,

    /// <summary>The caller belongs to this workspace and holds the permission.</summary>
    Granted = 2,
}
