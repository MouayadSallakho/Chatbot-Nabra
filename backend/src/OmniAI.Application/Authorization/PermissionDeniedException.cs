using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// Raised when a member of a workspace attempts something their roles do not
/// permit.
/// </summary>
/// <remarks>
/// <strong>This is the one refusal in the platform that admits the resource
/// exists.</strong> Every other kind of denial is reported as not-found, so that
/// nothing can be probed. This one is different on purpose: the caller has
/// already proved they belong to the workspace, so telling them a workspace they
/// are a member of exists reveals nothing they did not know. What it buys is a
/// person who can act -- they can see they need a permission and ask somebody for
/// it, instead of staring at a resource that appears to have vanished.
/// <para>
/// <strong>It is deliberately distinct from "you are not a member".</strong> The
/// two produce different statuses and they must: not a member is <c>404</c>,
/// because for that caller the workspace does not exist and saying otherwise would
/// confirm somebody else's workspace is real. Lacking a permission is <c>403</c>.
/// Collapsing them either way would be a mistake in one direction or the other --
/// answering <c>403</c> to a stranger would confirm the workspace exists, and
/// answering <c>404</c> to a member would tell them their own workspace had
/// disappeared.
/// </para>
/// <para>
/// <strong>It names the permission, and nothing else.</strong> The code is
/// already public -- it is in the catalogue every role editor reads -- so naming
/// it discloses nothing while telling the caller exactly what is missing. It
/// carries no role name, no member list, no tenant name and no hint about which
/// resource was addressed, so a denial cannot become a way to learn how a
/// workspace is organised or whether a particular record is there.
/// </para>
/// <para>
/// Transport-neutral by construction: no status code, no <c>ProblemDetails</c>,
/// no HTTP vocabulary. The API decides that this means <c>403</c>.
/// </para>
/// </remarks>
public sealed class PermissionDeniedException : Exception
{
    private const string DefaultMessage =
        "You do not have permission to perform this action in this workspace.";

    public PermissionDeniedException()
        : base(DefaultMessage)
    {
    }

    public PermissionDeniedException(string message)
        : base(message)
    {
    }

    public PermissionDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The permission the caller would have needed.</summary>
    public string? RequiredPermission { get; private init; }

    /// <summary>Creates the exception for a known missing permission.</summary>
    public static PermissionDeniedException ForPermission(PermissionCode permission) =>
        new($"{DefaultMessage} It requires the '{permission}' permission.")
        {
            RequiredPermission = permission.Value,
        };
}
