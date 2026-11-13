namespace OmniAI.Domain.Conversations;

/// <summary>
/// One communication item inside a <see cref="Conversation"/>. Immutable once
/// created.
/// </summary>
/// <remarks>
/// A message is a record of something that was said. Rewriting it would rewrite
/// history, so there are no setters and no edit operation: every property is
/// get-only and set once in the constructor. Correcting a message is a new
/// message, not a mutation of the old one.
/// <para>
/// Instances are created only by <see cref="Conversation"/> -- the factory is
/// <c>internal</c> -- so a message cannot exist detached from the conversation
/// that owns it, and cannot be constructed with a conversation id that does not
/// match its parent.
/// </para>
/// </remarks>
public sealed class Message
{
    /// <summary>
    /// Maximum text length, in UTF-16 code units, measured after trimming.
    /// </summary>
    /// <remarks>
    /// This is a platform resource-safety bound, deliberately <b>not</b> any
    /// channel's limit. It sits roughly eight times above the largest current
    /// channel cap, so the Domain never rejects text a channel would have
    /// accepted, while still bounding memory, storage, log and render cost at
    /// about 64 KB per message. Per-channel outbound limits -- which differ per
    /// provider and change without notice -- are the responsibility of the
    /// channel adapters, where provider knowledge belongs.
    /// </remarks>
    public const int MaxTextLength = 32_000;

    private Message(
        MessageId id,
        ConversationId conversationId,
        MessageSender sender,
        MessageContentType contentType,
        string text,
        DateTimeOffset sentAt)
    {
        Id = id;
        ConversationId = conversationId;
        Sender = sender;
        ContentType = contentType;
        Text = text;
        SentAt = sentAt;
    }

    /// <summary>Identity.</summary>
    public MessageId Id { get; }

    /// <summary>The conversation this message belongs to.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>Who produced this message.</summary>
    public MessageSender Sender { get; }

    /// <summary>What kind of content this message carries.</summary>
    public MessageContentType ContentType { get; }

    /// <summary>
    /// The message text. Always present, because both content types currently
    /// defined are textual. When non-textual content arrives this becomes
    /// conditional on <see cref="ContentType"/>.
    /// </summary>
    public string Text { get; }

    /// <summary>When the message was sent, in UTC.</summary>
    public DateTimeOffset SentAt { get; }

    /// <summary>
    /// Creates a message. Internal by design: only <see cref="Conversation"/> may
    /// call it, so a message cannot be created outside the aggregate that owns it.
    /// </summary>
    /// <exception cref="DomainValidationException">The text breaks a content rule.</exception>
    internal static Message Create(
        ConversationId conversationId,
        MessageSender sender,
        MessageContentType contentType,
        string? text,
        DateTimeOffset sentAt) =>
        new(
            MessageId.New(),
            conversationId,
            sender,
            contentType,
            NormaliseText(text),
            sentAt);

    /// <summary>
    /// Validates and normalises message text: surrounding whitespace is trimmed
    /// and everything inside is preserved exactly, because interior spacing,
    /// line breaks and indentation carry meaning in a message in a way they do
    /// not in a name.
    /// </summary>
    private static string NormaliseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainValidationException("Message text is required.")
            {
                Code = "conversations.message.required",
            };
        }

        string normalised = text.Trim();

        if (normalised.Length > MaxTextLength)
        {
            throw new DomainValidationException(
                $"Message text must be at most {MaxTextLength} characters; " +
                $"the supplied text has {normalised.Length}. It is not truncated.")
            {
                Code = "conversations.message.too_long",
            };
        }

        foreach (char character in normalised)
        {
            // Line breaks and tabs are ordinary message content. Other control
            // characters are not: they carry no meaning here and would corrupt
            // logs, transcripts and rendered output.
            if (char.IsControl(character) && character is not ('\n' or '\r' or '\t'))
            {
                throw new DomainValidationException(
                    "Message text must not contain control characters other than tab, carriage return and line feed.")
                {
                    Code = "conversations.message.control_characters",
                };
            }
        }

        return normalised;
    }
}
