namespace OmniAI.Domain;

/// <summary>
/// Raised when input violates a domain invariant.
/// </summary>
/// <remarks>
/// This is the single exception type used for domain rule violations across the
/// whole Domain layer. It exists so that an outer layer can distinguish "the
/// caller supplied a value the business rules reject" -- which is a client error
/// -- from a <see cref="ArgumentNullException"/> or any other BCL exception,
/// which signals a programming defect. Deliberately not a hierarchy: one type
/// keeps translation at the edge trivial and avoids a catalogue of exception
/// classes that carry no extra behaviour.
/// <para>
/// <strong>One type, many codes (Phase 37.5a).</strong> A throw site names the
/// rule it enforces with <see cref="Code"/>, a stable dotted word such as
/// <c>ai-agents.name.required</c>. The message stays the English sentence for a
/// person; the code is what a client branches on and translates. Still not a
/// hierarchy: a code is data, and a catalogue of classes carrying one string each
/// would be exactly the catalogue this type exists to avoid.
/// </para>
/// </remarks>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException()
    {
    }

    public DomainValidationException(string message)
        : base(message)
    {
    }

    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The stable code of the rule that refused, or <see langword="null"/> where
    /// the throw site has not been given one yet.
    /// </summary>
    /// <remarks>
    /// Published as the problem response's <c>code</c>. A site without one is
    /// answered with the generic <c>validation.failed</c>, so a client always has
    /// a code; <c>docs/architecture/problem-codes.md</c> lists every specific one.
    /// </remarks>
    public string? Code { get; init; }
}
