namespace OmniAI.Application.Concurrency;

/// <summary>
/// An opaque marker for "the version of this resource that I read".
/// </summary>
/// <remarks>
/// <para>
/// The Application passes these around and compares them for equality. It never
/// parses one, never performs arithmetic on one, and has no idea what is inside.
/// Everything that gives the value meaning -- how it is produced from stored
/// state, and how a presented value is turned back into something a database can
/// check -- belongs to Infrastructure, which is the only layer that knows this
/// platform stores its concurrency state in PostgreSQL's <c>xmin</c> system
/// column.
/// </para>
/// <para>
/// <strong>One type, not a framework.</strong> There is no
/// <c>IVersioned&lt;T&gt;</c>, no generic entity-version abstraction, no
/// reflection, no middleware and no interface. AI agents are the only resource
/// with conditional requests today. The type is written generically because a
/// version marker genuinely has no aggregate-specific content -- not because a
/// second user is planned -- and if one arrives it can reuse this as it stands.
/// </para>
/// <para>
/// <strong>Not in the Domain, deliberately.</strong> An agent's identity, name,
/// instructions and lifecycle are business facts; which stored revision somebody
/// happened to read is not. Putting this in the Domain would make the aggregate
/// aware of a persistence and transport concern it has no opinion about.
/// </para>
/// <para>
/// <strong>Not a secret.</strong> It is handed to clients as an HTTP
/// <c>ETag</c> and comes back in <c>If-Match</c>. It is opaque so that clients
/// cannot build logic on its internals and so the encoding can change without
/// breaking them -- not to conceal anything. No signing or encryption is applied,
/// because there is nothing here worth protecting and a MAC would imply
/// otherwise.
/// </para>
/// </remarks>
public readonly record struct ConcurrencyToken
{
    private ConcurrencyToken(string value) => Value = value;

    /// <summary>
    /// The opaque value. Safe to transport and to compare; meaningless to read.
    /// </summary>
    public string Value { get; }

    /// <summary>True for the uninitialised <c>default</c> value.</summary>
    /// <remarks>
    /// C# always permits <c>default(ConcurrencyToken)</c> for a struct, so code
    /// that accepts one from outside should reject an empty one explicitly rather
    /// than treat it as a version that could match something.
    /// </remarks>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Wraps an already-produced opaque value.</summary>
    /// <exception cref="ArgumentException">The value is null or blank.</exception>
    public static ConcurrencyToken From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A concurrency token must not be blank.", nameof(value));
        }

        return new ConcurrencyToken(value);
    }

    public override string ToString() => Value;
}
