namespace OmniAI.Application.Concurrency;

/// <summary>
/// Something as it was read from storage, together with the version it was read
/// at.
/// </summary>
/// <typeparam name="T">The aggregate or read model that was read.</typeparam>
/// <param name="Value">What was read.</param>
/// <param name="Version">The version marker for the revision that was read.</param>
/// <remarks>
/// <para>
/// The two travel together because they are only meaningful together: a caller
/// that has the aggregate but not the version cannot make a conditional write,
/// and a version without the thing it came from says nothing.
/// </para>
/// <para>
/// <strong>Generic because Phase 13 produced a second user, and no sooner.</strong>
/// Phase 7 wrote this shape by hand as <c>VersionedAiAgent</c> and recorded that a
/// second aggregate needing client-think-time concurrency would be the trigger to
/// revisit it. Tenant roles are that second aggregate, and the choice was between
/// a parallel hand-written <c>VersionedTenantRole</c> or one type used twice. Two
/// spellings of an identical idea is the thing that becomes three spellings, so
/// the hand-written records were replaced by this and their names retired.
/// </para>
/// <para>
/// <strong>It is one record, not a framework.</strong> There is no
/// <c>IVersioned&lt;T&gt;</c>, no constraint on <typeparamref name="T"/>, no base
/// class, no reflection and no registration. Nothing implements an interface to
/// participate, and nothing is discovered -- a repository returns one because its
/// signature says so. The generic parameter carries no requirement precisely
/// because a version marker has no aggregate-specific content: the same four
/// bytes describe a row of any table.
/// </para>
/// <para>
/// <strong>Deliberately not in the Domain.</strong> An agent's identity, a role's
/// permissions and a tenant's name are business facts; which stored revision
/// somebody happened to read is not. Putting this in the Domain would make every
/// aggregate aware of a persistence and transport concern it has no opinion
/// about.
/// </para>
/// <para>
/// <strong>What was deliberately not extracted.</strong> Each slice keeps its own
/// precondition check and its own version-mismatch exception, because each
/// carries the identity of the thing that moved and explains it in that thing's
/// own words. A shared <c>ResourceVersionMismatchException</c> would have to
/// describe an agent and a role in the same sentence, and a shared checker would
/// need an exception factory passed into it -- more machinery than the two lines
/// it would save, in exchange for messages that say less.
/// </para>
/// </remarks>
public sealed record Versioned<T>(T Value, ConcurrencyToken Version);
