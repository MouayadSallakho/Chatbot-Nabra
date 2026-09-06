namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Names that the schema and the queries must agree on.
/// </summary>
internal static class PersistenceSchema
{
    /// <summary>
    /// Shadow property holding the value that orders a message within its
    /// conversation. Not a Domain concept: the Domain's identity is
    /// <c>MessageId</c>, and nothing in the Domain reads this.
    /// </summary>
    /// <remarks>
    /// It exists because reload order cannot be derived from anything else.
    /// <c>Message.SentAt</c> is deliberately not monotonic, so ordering by it
    /// would reorder history. <c>MessageId</c> cannot serve either: it is a
    /// UUIDv7, which orders only to the millisecond and carries random bits
    /// below that, so messages appended within the same millisecond sort
    /// arbitrarily.
    /// <para>
    /// The value is allocated from <see cref="MessageIngestionSequenceName"/>,
    /// a PostgreSQL sequence, by <c>OmniAiDbContext</c> immediately before
    /// insert. Only relative order is meaningful; the numbers are global rather
    /// than per-conversation, so they are sparse within any one conversation and
    /// gaps are expected.
    /// </para>
    /// </remarks>
    public const string MessageIngestionSequence = "IngestionSequence";

    /// <summary>
    /// The PostgreSQL sequence that allocates message ingestion order.
    /// </summary>
    /// <remarks>
    /// A sequence rather than a counter on the conversation row: <c>nextval</c>
    /// is atomic, takes no row lock, never returns a value twice even across
    /// concurrent sessions, and does not touch the conversation row -- so it
    /// cannot disturb that row's <c>xmin</c> concurrency token. A per-conversation
    /// counter column would have to be updated on every append, which would bump
    /// the conversation's <c>xmin</c> and make unrelated writers fail.
    /// </remarks>
    public const string MessageIngestionSequenceName = "message_ingestion_sequence";

    /// <summary>
    /// Shadow property holding the case-insensitive form of a role's name, which
    /// the tenant-scoped unique index is built on.
    /// </summary>
    /// <remarks>
    /// A shadow property rather than a member of <c>TenantRoleName</c>'s mapping,
    /// because a value converter fills one column and this name needs two: the
    /// display form a person reads, and the form uniqueness is decided on. The
    /// role repository sets it from <c>TenantRoleName.Normalised</c> on every
    /// write, so normalisation has exactly one implementation -- the Domain's --
    /// rather than one in .NET and a second in PostgreSQL's <c>upper()</c>, which
    /// are not guaranteed to agree for non-ASCII names under every collation.
    /// </remarks>
    public const string NormalisedRoleName = "NormalisedName";

    /// <summary>
    /// The shadow property holding an invited address in its comparison form.
    /// </summary>
    /// <remarks>
    /// A shadow property for the same reason the normalised role name is one: the
    /// Domain already derives it, and a second column the Domain also knew about
    /// would be two places that could disagree about what canonical means.
    /// </remarks>
    public const string CanonicalEmail = "CanonicalEmail";

    /// <summary>
    /// PostgreSQL's per-row transaction id system column, used as an optimistic
    /// concurrency token. It already exists on every table; it is never created
    /// by a migration.
    /// </summary>
    public const string XminConcurrencyToken = "xmin";
}
