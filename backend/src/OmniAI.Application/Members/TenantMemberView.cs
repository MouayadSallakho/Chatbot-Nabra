using OmniAI.Domain.Authorization;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Members;

/// <summary>One role a member holds, as the Team screens need it.</summary>
/// <param name="Id">The role's identity.</param>
/// <param name="Name">Its display name.</param>
/// <param name="IsSystem">Whether it is the protected Owner role.</param>
public sealed record TenantMemberRole(Guid Id, string Name, bool IsSystem);

/// <summary>
/// One member of a workspace, in a list.
/// </summary>
/// <param name="UserId">The account's identity.</param>
/// <param name="Email">The address they sign in with.</param>
/// <param name="JoinedAt">When they became a member.</param>
/// <param name="Roles">The roles they hold, Owner first then by name.</param>
/// <param name="IsOwner">
/// Whether they hold the protected Owner role. Published beside the roles so a
/// client does not have to know which role names are protected in order to
/// decide whether to offer an editor.
/// </param>
/// <remarks>
/// <strong>What is deliberately absent.</strong> No password hash, no security
/// stamp, no lockout counters, no confirmation flags, no authentication metadata
/// of any kind. The Team screen needs to say who somebody is and what they can
/// do; nothing about how they prove who they are belongs in an administrator's
/// list, and a field that is never displayed is only a field waiting to be
/// logged.
/// <para>
/// <strong>Roles, not permissions.</strong> A row carries the names of the roles
/// somebody holds rather than the union those roles expand to. The expansion is
/// the same for everybody with the same roles, it is long, and putting it on
/// every row of a paged list would send the catalogue repeatedly to render a
/// column nobody reads. The detail view is where a specific member's access is
/// examined.
/// </para>
/// </remarks>
public sealed record TenantMemberSummary(
    Guid UserId,
    string Email,
    DateTimeOffset JoinedAt,
    IReadOnlyList<TenantMemberRole> Roles,
    bool IsOwner);

/// <summary>
/// One member, in full.
/// </summary>
/// <param name="UserId">The account's identity.</param>
/// <param name="TenantId">The workspace they belong to.</param>
/// <param name="Email">The address they sign in with.</param>
/// <param name="JoinedAt">When they became a member.</param>
/// <param name="AccessUpdatedAt">When their roles last changed.</param>
/// <param name="Roles">The roles they hold.</param>
/// <param name="IsOwner">Whether they hold the protected Owner role.</param>
/// <param name="Permissions">
/// Everything those roles add up to, in catalogue order.
/// </param>
/// <remarks>
/// <strong>The permission union is published here and nowhere else.</strong> It
/// is what an administrator actually wants to know before changing somebody's
/// access -- "what can this person do today" -- and it is the same question the
/// authority ceiling is decided on, so showing it makes a refusal
/// comprehensible rather than mysterious. It is derived, never stored, and never
/// accepted as input.
/// <para>
/// No version travels in this record. Which stored revision it was read at goes
/// beside it in a <c>Versioned&lt;T&gt;</c>, so it reaches the client in the
/// <c>ETag</c> header rather than the body -- the same arrangement roles and
/// agents already use.
/// </para>
/// </remarks>
public sealed record TenantMemberView(
    Guid UserId,
    Guid TenantId,
    string Email,
    DateTimeOffset JoinedAt,
    DateTimeOffset AccessUpdatedAt,
    IReadOnlyList<TenantMemberRole> Roles,
    bool IsOwner,
    IReadOnlyList<PermissionCode> Permissions);

/// <summary>
/// A member as the store holds them, before it is decided what to publish.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="Email">Their address.</param>
/// <param name="JoinedAt">When they joined.</param>
/// <param name="AccessUpdatedAt">When their roles last changed.</param>
/// <param name="Roles">The role aggregates they hold.</param>
/// <remarks>
/// Carries real <see cref="TenantRole"/> aggregates rather than names, because
/// every authority decision about a member -- may this person be reassigned, is
/// their current authority within mine -- needs what those roles actually permit,
/// and an Owner permits everything while storing nothing. A projection to names
/// would lose exactly the information the security rules run on.
/// </remarks>
public sealed record TenantMemberRecord(
    UserId UserId,
    EmailAddress Email,
    DateTimeOffset JoinedAt,
    DateTimeOffset AccessUpdatedAt,
    IReadOnlyList<TenantRole> Roles);
