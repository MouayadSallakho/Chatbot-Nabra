namespace OmniAI.Domain.Knowledge;

/// <summary>
/// An opaque marker for "the way these vectors were produced".
/// </summary>
/// <remarks>
/// <para>
/// <strong>The domain never reads it.</strong> It does not know what an embedding
/// is, which provider made one, or what a dimension count means -- and this type
/// deliberately keeps it that way. What the domain knows is narrower and enough:
/// vectors produced under two different markers are not comparable, so an index
/// built under one is not a valid index under another.
/// </para>
/// <para>
/// <strong>Why this matters more than it sounds.</strong> Cosine distance between
/// two vectors from different models is a perfectly well-formed number. It is
/// also meaningless. Without a stored marker, changing the embedding model would
/// not break anything visibly -- retrieval would keep working and would keep
/// returning confidently ranked nonsense, drawn from whichever documents happened
/// to be re-indexed most recently. The marker turns that silent corruption into
/// an explicit "this document needs indexing again".
/// </para>
/// <para>
/// The value is chosen by whatever configures the provider and is treated as
/// opaque everywhere above it. No provider or model name appears in this project.
/// </para>
/// </remarks>
public sealed record EmbeddingProfileId
{
    public const int MaxLength = 128;

    private EmbeddingProfileId(string value) => Value = value;

    public string Value { get; }

    public static EmbeddingProfileId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("An embedding profile identifier is required.");
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"An embedding profile identifier must be at most {MaxLength} characters; " +
                $"the supplied identifier has {normalised.Length}.");
        }

        foreach (char character in normalised)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                throw new DomainValidationException(
                    "An embedding profile identifier must not contain whitespace or control "
                    + "characters.");
            }
        }

        return new EmbeddingProfileId(normalised);
    }

    public override string ToString() => Value;
}
