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
}
