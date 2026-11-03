namespace OmniAI.Domain.Knowledge;

/// <summary>
/// What a knowledge document is called.
/// </summary>
/// <remarks>
/// One line, because it is a label rather than content: it appears in a list, in
/// an assignment checkbox and beside a retrieval result, and every one of those
/// places assumes a single line that fits. The document's actual text is
/// <see cref="KnowledgeContent"/>, which is where line breaks belong.
/// </remarks>
public sealed record KnowledgeTitle
{
    public const int MaxLength = 200;

    private KnowledgeTitle(string value) => Value = value;

    public string Value { get; }

    public static KnowledgeTitle Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A knowledge document needs a title.")
            {
                Code = "knowledge.title.required",
            };
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A knowledge document title must be at most {MaxLength} characters; " +
                $"the supplied title has {normalised.Length}. It is not truncated.")
            {
                Code = "knowledge.title.too_long",
            };
        }

        foreach (char character in normalised)
        {
            // No exemption for line breaks or tabs here, unlike the content: a
            // title is one line by definition, and a smuggled newline would break
            // every list and every log line it appears in.
            if (char.IsControl(character))
            {
                throw new DomainValidationException(
                    "A knowledge document title must be a single line and must not contain "
                    + "control characters.")
                {
                    Code = "knowledge.title.control_characters",
                };
            }
        }

        return new KnowledgeTitle(normalised);
    }

    public override string ToString() => Value;
}
