using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// The model's description of <c>knowledge_chunks</c>: one indexed passage of a
/// document and its embedding.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It exists so the table can be derived from the model, and for nothing
/// else.</strong> Chunks are written by <c>KnowledgeIndexStore</c> and read by
/// <c>KnowledgeSearchStore</c>, both in parameterised SQL, exactly as in Phase 17.
/// No repository materialises this type and the context exposes no set for it.
/// </para>
/// <para>
/// Only the key and relationship columns are CLR properties. The passage text,
/// the embedding, its profile and the creation instant are shadow properties in
/// <c>KnowledgeChunkRowConfiguration</c>, so there is not even a field on this
/// class that could carry a vector into an object graph.
/// </para>
/// </remarks>
internal sealed class KnowledgeChunkRow
{
    public Guid Id { get; set; }

    /// <summary>The workspace, copied from the document so the composite key can check it.</summary>
    public TenantId TenantId { get; set; }

    public KnowledgeDocumentId KnowledgeDocumentId { get; set; }

    /// <summary>The passage's position in its document, from zero.</summary>
    public int Ordinal { get; set; }
}
