using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// The precondition check every conditional role mutation performs before it
/// touches the aggregate.
/// </summary>
/// <remarks>
/// <para>
/// One method, shared by the two mutating role handlers. It is not a framework
/// and not a pipeline: no interface, no generic parameter, no registration and no
/// reflection -- a handler calls it the way it would call any other helper.
/// </para>
/// <para>
/// <strong>Why this exists at all, when the database also checks.</strong> The
/// storage layer refuses a write whose expected version no longer matches, which
/// covers every mutation that actually writes something. It cannot cover a
/// mutation that writes <em>nothing</em>: replacing a role's permissions with the
/// set it already has changes no row, so no statement is issued and there is no
/// predicate to fail. Without this check, a caller holding a hopelessly stale
/// version could send that request and receive a cheerful success -- a
/// precondition silently skipped because the operation happened to be a no-op.
/// The same reasoning the AI agent slice records, and the reason both keep their
/// own two-line check rather than sharing one behind an exception factory.
/// </para>
/// <para>
/// <strong>Why it runs before the protected-role rule.</strong> A caller whose
/// version is stale has not seen the role as it is now, so they are told that
/// first -- even when the operation would also have been refused for being aimed
/// at Owner. Reporting the protection rule instead would answer a question they
/// did not ask, about a role they have never seen, and would confirm to somebody
/// holding an out-of-date version exactly which role they had hold of.
/// </para>
/// </remarks>
internal static class TenantRoleConcurrency
{
    /// <summary>
    /// Throws unless the stored role is still at the version the caller expected.
    /// </summary>
    /// <exception cref="TenantRoleVersionMismatchException">The versions differ.</exception>
    public static void RequireExpectedVersion(
        Versioned<TenantRole> stored,
        ConcurrencyToken expected)
    {
        ArgumentNullException.ThrowIfNull(stored);

        // Ordinal equality on an opaque value. Nothing here parses the token,
        // compares it for ordering, or knows what it is made of.
        if (stored.Version != expected)
        {
            throw TenantRoleVersionMismatchException.ForRole(stored.Value.Id.Value);
        }
    }
}
