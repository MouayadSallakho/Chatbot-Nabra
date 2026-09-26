using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// The Identity <c>users</c> table as a foreign-key target for Domain rows that
/// hold a <see cref="UserId"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> Memberships and notifications point at
/// <c>users(id)</c>. EF relates two entity types only when the key properties
/// on both sides have the same CLR type, and here they cannot: the Domain holds
/// a <see cref="UserId"/>, while the Identity user's key is
/// <c>IdentityUser&lt;Guid&gt;.Id</c>, a plain <see cref="Guid"/> on a framework
/// type this platform does not reshape. EF refuses the relationship outright,
/// and it refuses a second property mapped to the same column on the Identity
/// entity.
/// </para>
/// <para>
/// So the foreign keys target this instead: the same table, with its key typed
/// the way the Domain types it. It is mapped with
/// <c>ExcludeFromMigrations</c>, EF's supported way to reference a table that
/// another mapping owns, so migrations emit the foreign keys and never try to
/// create, alter or drop <c>users</c>. Identity's own mapping remains the only
/// one that creates the table.
/// </para>
/// <para>
/// The table name is schema-qualified (<c>public.users</c>) only so EF treats
/// this as a reference rather than as a second entity sharing Identity's
/// table, which it would reject. PostgreSQL resolves both names to the same
/// table. Nothing materialises this type: the context exposes no set for it and
/// no repository names it.
/// </para>
/// </remarks>
internal sealed class UserReferenceRow
{
    public UserId Id { get; set; }
}
