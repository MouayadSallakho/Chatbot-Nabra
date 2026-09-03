namespace OmniAI.Domain;

/// <summary>
/// Raised when an operation is well-formed but not permitted by the current
/// state of the aggregate it targets.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DomainValidationException"/> because the two describe
/// genuinely different failures: invalid input is a request that could never
/// succeed as written, while an invalid state transition is a well-formed request
/// that would succeed once the aggregate is in another state. Collapsing them
/// would lose that distinction and force every caller to re-derive it.
/// <para>
/// The Domain states the distinction and stops there. How each category is
/// surfaced to a client -- status codes, error contracts, retry advice -- is a
/// transport concern owned by the Application and API layers.
/// </para>
/// <para>
/// This is the second and, for now, last exception type in the Domain. The rule
/// remains one type per meaningful category of failure, never one per rule.
/// </para>
/// </remarks>
public sealed class DomainStateException : Exception
{
    public DomainStateException()
    {
    }

    public DomainStateException(string message)
        : base(message)
    {
    }

    public DomainStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
