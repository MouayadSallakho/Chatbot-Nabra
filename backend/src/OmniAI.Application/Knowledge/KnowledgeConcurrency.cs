using OmniAI.Application.Concurrency;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// The one place a supplied entity tag is checked against a stored document.
/// </summary>
/// <remarks>
/// A helper rather than four copies of the same two lines, so no handler can be
/// written that forgets the check. It is the same shape as the AI agent
/// equivalent, deliberately: two ways of expressing the same precondition would
/// eventually behave differently.
/// </remarks>
internal static class KnowledgeConcurrency
{
    public static void RequireExpectedVersion(
        Versioned<KnowledgeDocument> stored,
        ConcurrencyToken expected)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Version != expected)
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(stored.Value.Id.Value);
        }
    }
}
