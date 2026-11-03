using System.Globalization;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// Identity of a knowledge source.
/// </summary>
/// <remarks>
/// A distinct type rather than a bare <see cref="Guid"/>, for the same reason
/// every other identity here is: a method taking a document id and a source id
/// will one day be called with them the wrong way round, and nothing would
/// complain.
/// </remarks>
public readonly record struct KnowledgeSourceId
{
    private KnowledgeSourceId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A fresh identity, ordered by creation time.</summary>
    public static KnowledgeSourceId New() => new(Guid.CreateVersion7());

    public static KnowledgeSourceId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Knowledge source id must not be an empty GUID.")
            {
                Code = "knowledge.source.invalid",
            };
        }

        return new KnowledgeSourceId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
