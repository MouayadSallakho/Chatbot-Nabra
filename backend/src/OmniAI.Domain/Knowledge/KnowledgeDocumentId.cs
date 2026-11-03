using System.Globalization;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// Identity of a knowledge document.
/// </summary>
/// <remarks>
/// A distinct type rather than a bare <see cref="Guid"/>, for the same reason
/// every other identity here is: a method taking three GUIDs will one day be
/// called with them in the wrong order, and nothing will complain.
/// </remarks>
public readonly record struct KnowledgeDocumentId
{
    private KnowledgeDocumentId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A fresh identity, ordered by creation time.</summary>
    public static KnowledgeDocumentId New() => new(Guid.CreateVersion7());

    public static KnowledgeDocumentId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Knowledge document id must not be an empty GUID.")
            {
                Code = "knowledge.id.invalid",
            };
        }

        return new KnowledgeDocumentId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
