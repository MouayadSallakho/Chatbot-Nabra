using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.SearchAgentKnowledge;

public sealed record SearchAgentKnowledgeQuery(Guid TenantId, Guid AiAgentId, string? Question);

/// <summary>One retrieval result, as the admin tester shows it.</summary>
/// <remarks>
/// <strong>No vector, and there never will be one.</strong> What comes back is
/// which document answered, where in it the passage sits, the text itself and how
/// close it was. Publishing the embedding would hand anybody who can read one
/// document a way to compare it numerically against every other, and would be
/// several kilobytes per result that nothing on the screen could use.
/// </remarks>
public sealed record KnowledgeSearchHit(
    Guid KnowledgeDocumentId,
    string Title,
    int Ordinal,
    string Excerpt,
    int Rank,
    double Similarity);

/// <summary>What a question would have retrieved.</summary>
public sealed record KnowledgeSearchResult(IReadOnlyList<KnowledgeSearchHit> Hits);

/// <summary>
/// Runs the same retrieval a live turn would, and shows the result.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is the same code path, deliberately.</strong> A tester that ran its
/// own query would tell somebody what a different system would have found. This
/// goes through the retriever a conversation uses, with the same filters, the
/// same ranking and the same character budget, so what it shows is what an agent
/// would actually have been given.
/// </para>
/// <para>
/// <strong>It costs one embedding call, and only when there is something to
/// search.</strong> The retriever asks whether the agent has any eligible chunk
/// first, so testing an agent with no ready knowledge is free.
/// </para>
/// </remarks>
public sealed class SearchAgentKnowledgeHandler(
    IAiAgentRepository agents,
    KnowledgeRetriever retriever,
    TenantAuthorizer authorizer)
{
    /// <summary>The longest question the tester accepts.</summary>
    public const int MaxQuestionLength = 2_000;

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly KnowledgeRetriever _retriever =
        retriever ?? throw new ArgumentNullException(nameof(retriever));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    public async Task<KnowledgeSearchResult?> HandleAsync(
        SearchAgentKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        AiAgentId agentId = AiAgentId.From(query.AiAgentId);
        string question = RequireQuestion(query.Question);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        // The agent is read before anything is embedded, so testing against an
        // agent that does not exist is a not-found rather than a paid call
        // followed by an empty result.
        if (await _agents.FindAsync(tenantId, agentId, cancellationToken).ConfigureAwait(false)
            is null)
        {
            return null;
        }

        IReadOnlyList<KnowledgeExcerpt> excerpts = await _retriever
            .RetrieveAsync(tenantId, agentId, question, limit: null, cancellationToken)
            .ConfigureAwait(false);

        KnowledgeSearchHit[] hits = new KnowledgeSearchHit[excerpts.Count];

        for (int index = 0; index < excerpts.Count; index++)
        {
            KnowledgeExcerpt excerpt = excerpts[index];

            hits[index] = new KnowledgeSearchHit(
                excerpt.DocumentId,
                excerpt.DocumentTitle,
                excerpt.Ordinal,
                excerpt.Text,
                index + 1,
                excerpt.Similarity);
        }

        return new KnowledgeSearchResult(hits);
    }

    private static string RequireQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new DomainValidationException("A question is required to test retrieval.");
        }

        string trimmed = question.Trim();

        if (trimmed.Length > MaxQuestionLength)
        {
            throw new DomainValidationException(
                $"A test question must be at most {MaxQuestionLength} characters; "
                + $"the supplied question has {trimmed.Length}.");
        }

        return trimmed;
    }
}
