using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// A decision that one AI agent may draw on one knowledge document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Knowledge is not shared with every agent by default, and that is the
/// whole reason this type exists.</strong> A workspace can hold a public returns
/// policy and an internal escalation note side by side; a support agent answering
/// the public should have the first and must not have the second. Making the link
/// explicit means nobody has to remember to exclude anything -- an agent starts
/// with nothing and is given exactly what somebody chose to give it.
/// </para>
/// <para>
/// <strong>The workspace is carried on the row rather than inferred.</strong> It
/// is what lets the database refuse a link between one workspace's agent and
/// another workspace's document: the same value has to satisfy both foreign keys
/// at once, and a cross-workspace pair cannot. Isolation that depends on a
/// handler remembering to filter is isolation that lasts until somebody writes a
/// new handler.
/// </para>
/// <para>
/// An assignment says nothing about whether the document will actually be used.
/// Retrieval still requires it to be enabled and currently indexed; assigning a
/// document that is not ready is allowed, and the admin screens say plainly that
/// it will not answer anything yet.
/// </para>
/// </remarks>
public sealed class AgentKnowledgeAssignment
{
    private AgentKnowledgeAssignment(
        TenantId tenantId,
        AiAgentId aiAgentId,
        KnowledgeDocumentId knowledgeDocumentId,
        DateTimeOffset assignedAt)
    {
        TenantId = tenantId;
        AiAgentId = aiAgentId;
        KnowledgeDocumentId = knowledgeDocumentId;
        AssignedAt = assignedAt;
    }

    public TenantId TenantId { get; }

    public AiAgentId AiAgentId { get; }

    public KnowledgeDocumentId KnowledgeDocumentId { get; }

    public DateTimeOffset AssignedAt { get; }

    public static AgentKnowledgeAssignment Create(
        TenantId tenantId,
        AiAgentId aiAgentId,
        KnowledgeDocumentId knowledgeDocumentId,
        DateTimeOffset assignedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException(
                "A knowledge assignment must belong to a workspace.");
        }

        if (aiAgentId.IsEmpty)
        {
            throw new DomainValidationException("A knowledge assignment must name an AI agent.");
        }

        if (knowledgeDocumentId.IsEmpty)
        {
            throw new DomainValidationException(
                "A knowledge assignment must name a knowledge document.");
        }

        if (assignedAt == default)
        {
            throw new DomainValidationException(
                "The instant a knowledge document was assigned is required.");
        }

        return new AgentKnowledgeAssignment(
            tenantId,
            aiAgentId,
            knowledgeDocumentId,
            assignedAt.ToUniversalTime());
    }
}
