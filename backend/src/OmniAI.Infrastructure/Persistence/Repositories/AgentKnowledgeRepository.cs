using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>Which documents each agent may draw on.</summary>
/// <remarks>
/// The replacement and the agent's own row move together in one transaction. If
/// the agent's version has moved, the concurrency check fails, the transaction is
/// abandoned, and the assignments are exactly as they were -- rather than the
/// join table having been rewritten against an agent somebody else has since
/// reconfigured.
/// </remarks>
internal sealed class AgentKnowledgeRepository(OmniAiDbContext context) : IAgentKnowledgeRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<KnowledgeDocumentId>> ListAssignedAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default) =>
        await _context.AgentKnowledgeAssignments
            .Where(assignment =>
                assignment.TenantId == tenantId && assignment.AiAgentId == aiAgentId)
            .OrderBy(assignment => assignment.KnowledgeDocumentId)
            .Select(assignment => assignment.KnowledgeDocumentId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<ConcurrencyToken> ReplaceAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeDocumentId> documentIds,
        DateTimeOffset assignedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(documentIds);

        PropertyEntry<AiAgent, uint> version = _context.Entry(agent)
            .Property<uint>(PersistenceSchema.XminConcurrencyToken);

        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw AiAgentVersionMismatchException.ForAgent(agent.Id.Value);
        }

        version.OriginalValue = expectedRowVersion;

        List<AgentKnowledgeAssignment> existing = await _context.AgentKnowledgeAssignments
            .Where(assignment =>
                assignment.TenantId == agent.TenantId && assignment.AiAgentId == agent.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<KnowledgeDocumentId> wanted = [.. documentIds];

        // Rows that are staying are left completely alone, so the instant they
        // were first assigned survives. Deleting and rewriting the whole set would
        // rewrite that history every time somebody ticked one extra box.
        foreach (AgentKnowledgeAssignment assignment in existing)
        {
            if (!wanted.Remove(assignment.KnowledgeDocumentId))
            {
                _context.AgentKnowledgeAssignments.Remove(assignment);
            }
        }

        foreach (KnowledgeDocumentId documentId in wanted)
        {
            _context.AgentKnowledgeAssignments.Add(AgentKnowledgeAssignment.Create(
                agent.TenantId,
                agent.Id,
                documentId,
                assignedAt));
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            throw AiAgentVersionMismatchException.ForAgent(agent.Id.Value, failure);
        }

        return RowVersionTokens.Encode(version.CurrentValue);
    }
}
