namespace OmniAI.Application.Authorization;

/// <summary>
/// Raised when a workspace already has a role with the name being requested.
/// </summary>
/// <remarks>
/// <strong>Raised from the unique index, not only from the pre-check.</strong>
/// The create and rename use cases both look first, so an ordinary caller meets a
/// clean refusal without provoking a constraint violation. That check cannot be
/// the guarantee: two administrators creating <c>Support agent</c> at the same
/// instant both pass it, and only the database can decide which one wins. The
/// loser's insert is refused by the index and translated into this, so exactly one
/// role is stored and exactly one conflict is reported.
/// <para>
/// <strong>It says nothing about SQL.</strong> No constraint name, no SQLSTATE,
/// no table name, no <c>PostgresException</c> and no vendor. The message names the
/// role name the caller supplied -- which they already know, having just typed it
/// -- and nothing else. Infrastructure translates the provider's failure into this
/// before it crosses the boundary, precisely so a conflict cannot become a way to
/// learn how this platform stores things.
/// </para>
/// <para>
/// <strong>It is a conflict, not a stale version.</strong> Two roles cannot share
/// a name however recently the caller read the workspace, so re-reading and
/// retrying unchanged would fail again. That is what separates it from a
/// precondition failure, and why the API reports it as <c>409</c> rather than
/// <c>412</c> -- the caller must choose a different name, not refresh.
/// </para>
/// </remarks>
public sealed class TenantRoleNameAlreadyExistsException : Exception
{
    public TenantRoleNameAlreadyExistsException()
        : base("A role with that name already exists in this workspace.")
    {
    }

    public TenantRoleNameAlreadyExistsException(string message)
        : base(message)
    {
    }

    public TenantRoleNameAlreadyExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The name that was already taken, when the thrower knew it.</summary>
    public string? Name { get; private init; }

    /// <summary>Creates the exception for a known name.</summary>
    public static TenantRoleNameAlreadyExistsException ForName(
        string name,
        Exception? innerException = null)
    {
        string message =
            $"This workspace already has a role called '{name}'. Role names must be unique " +
            "within a workspace, ignoring capitalisation. Choose a different name.";

        return innerException is null
            ? new TenantRoleNameAlreadyExistsException(message) { Name = name }
            : new TenantRoleNameAlreadyExistsException(message, innerException) { Name = name };
    }
}
