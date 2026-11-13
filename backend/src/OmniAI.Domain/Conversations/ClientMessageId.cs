namespace OmniAI.Domain.Conversations;

/// <summary>
/// The client's own name for one message it is sending.
/// </summary>
/// <remarks>
/// <strong>This is an idempotency key, and it is the only thing standing between
/// a flaky network and a duplicated bill.</strong> A send that reaches the server
/// and whose response is lost looks, to the browser, exactly like a send that
/// never arrived — and to a proxy retry, a double click or a reconnect, exactly
/// like a new message. Because the client names the message before sending it,
/// every one of those repeats carries the same name and can be recognised as the
/// same turn rather than answered again at a provider's price.
/// <para>
/// <strong>Opaque to the platform.</strong> Nothing is parsed out of it and
/// nothing is inferred from it. It is compared for equality within one
/// conversation and never across conversations, so two visitors choosing the same
/// value collide with nobody. It is not an identifier the platform hands out, is
/// never returned as one, and orders nothing.
/// </para>
/// <para>
/// <strong>Bounded, because it arrives from an anonymous browser.</strong> A
/// length cap and a conservative alphabet keep an untrusted value from becoming a
/// storage cost, a log-injection vector or an index of unbounded width. A UUID is
/// the expected shape and fits comfortably.
/// </para>
/// </remarks>
public readonly record struct ClientMessageId
{
    /// <summary>The longest accepted value, in characters.</summary>
    /// <remarks>
    /// Comfortably above a 36-character UUID, so a client with its own id scheme
    /// is not forced to invent one, and far below anything that would make the
    /// unique index expensive.
    /// </remarks>
    public const int MaxLength = 64;

    private ClientMessageId(string value) => Value = value;

    /// <summary>The value as the client wrote it.</summary>
    public string Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Reads a client-supplied idempotency key.</summary>
    /// <exception cref="DomainValidationException">
    /// It is missing, too long, or contains anything outside the accepted
    /// alphabet.
    /// </exception>
    public static ClientMessageId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A client message id is required.")
            {
                Code = "conversations.client_message_id.required",
            };
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A client message id must be at most {MaxLength} characters; " +
                $"the supplied value has {trimmed.Length}.")
            {
                Code = "conversations.client_message_id.too_long",
            };
        }

        foreach (char character in trimmed)
        {
            // Letters, digits, hyphen and underscore. Enough for a UUID or any
            // sane opaque token, and narrow enough that the value cannot carry
            // whitespace, control characters or punctuation that would need
            // escaping wherever it is written down.
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                throw new DomainValidationException(
                    "A client message id must contain only letters, digits, hyphens and underscores.")
                {
                    Code = "conversations.client_message_id.invalid",
                };
            }
        }

        return new ClientMessageId(trimmed);
    }

    public override string ToString() => Value;
}
