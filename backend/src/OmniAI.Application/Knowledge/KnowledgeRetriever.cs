using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Ai;
using OmniAI.Application.Ai.Usage;

namespace OmniAI.Application.Knowledge;

/// <summary>One passage retrieved for a question.</summary>
/// <remarks>
/// <paramref name="Similarity"/> is one minus the cosine distance: 1 for a
/// perfect match, lower as the passage becomes less alike. It is shown in the
/// admin retrieval tester and never sent to a model, which is given the text and
/// nothing else -- a number a model cannot verify is a number it should not be
/// invited to reason about.
/// </remarks>
public sealed record KnowledgeExcerpt(
    Guid DocumentId,
    string DocumentTitle,
    int Ordinal,
    string Text,
    double Similarity);

/// <summary>
/// Finds the passages of a workspace's knowledge that bear on a question.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The cheap question is asked first.</strong> Before anything is
/// embedded, the store is asked whether this agent has a single eligible chunk.
/// An agent with no knowledge assigned -- which is every agent that existed
/// before this feature, and most agents after it -- costs one indexed count query
/// and not one paid provider call. Embedding first and discovering there was
/// nothing to search would charge every workspace for a capability they never
/// switched on.
/// </para>
/// <para>
/// <strong>Nothing here decides who may see what.</strong> The workspace, the
/// agent, the enabled flag and the index currency are all applied by the query in
/// the store, inside the database. This class chooses how many results to keep
/// and how much text to spend; it never widens what the query was allowed to
/// return.
/// </para>
/// </remarks>
public sealed class KnowledgeRetriever(
    IKnowledgeSearchStore store,
    ITextEmbeddingGenerator embeddings,
    KnowledgeRetrievalPolicy policy)
{
    private readonly IKnowledgeSearchStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly KnowledgeRetrievalPolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    /// <summary>
    /// The passages this agent has that are closest to the question, within the
    /// character budget.
    /// </summary>
    /// <param name="limit">
    /// How many results to keep, or <c>null</c> for the policy's own top-K. The
    /// admin retrieval tester passes its own so somebody diagnosing why an answer
    /// was wrong can see a little further down the ranking than a turn would.
    /// </param>
    public async Task<IReadOnlyList<KnowledgeExcerpt>> RetrieveAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        string? query,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        int keep = limit ?? _policy.TopK;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keep);

        if (!await _store
            .HasEligibleChunksAsync(tenantId, aiAgentId, _embeddings.Profile.Id, cancellationToken)
            .ConfigureAwait(false))
        {
            return [];
        }

        string trimmed = Shorten(query.Trim());

        // Reached only past the eligibility check above: a workspace with
        // nothing to retrieve from never embeds a question, and therefore never
        // pays for one.
        IReadOnlyList<EmbeddingVector> vectors = await _embeddings
            .GenerateAsync(
                [trimmed],
                AiUsageContext.For(tenantId, AiUsagePurpose.ConversationRetrievalEmbedding),
                cancellationToken)
            .ConfigureAwait(false);

        if (vectors is null || vectors.Count != 1 || vectors[0] is null)
        {
            throw new EmbeddingResponseInvalidException(
                "The embedding provider did not return exactly one vector for the question. "
                + "No knowledge was retrieved.");
        }

        IReadOnlyList<KnowledgeMatch> matches = await _store
            .SearchAsync(
                tenantId,
                aiAgentId,
                _embeddings.Profile.Id,
                vectors[0],
                keep,
                cancellationToken)
            .ConfigureAwait(false);

        return WithinBudget(matches);
    }

    /// <summary>
    /// Keeps the best results that fit, dropping whole chunks from the bottom.
    /// </summary>
    /// <remarks>
    /// It stops at the first result that does not fit rather than continuing to
    /// look for a smaller one further down. Skipping past a more relevant passage
    /// to include a less relevant one because it happened to be shorter would make
    /// the ranking mean something other than what it says.
    /// </remarks>
    private List<KnowledgeExcerpt> WithinBudget(IReadOnlyList<KnowledgeMatch> matches)
    {
        if (matches is null || matches.Count == 0)
        {
            return [];
        }

        List<KnowledgeExcerpt> kept = [];
        int characters = 0;

        foreach (KnowledgeMatch match in matches)
        {
            if (characters + match.Text.Length > _policy.MaxContextCharacters)
            {
                break;
            }

            kept.Add(new KnowledgeExcerpt(
                match.DocumentId,
                match.DocumentTitle,
                match.Ordinal,
                match.Text,
                1d - match.Distance));

            characters += match.Text.Length;
        }

        return kept;
    }

    /// <summary>
    /// Keeps the question inside what the provider accepts as one input.
    /// </summary>
    /// <remarks>
    /// The only place in this feature where text is shortened, and it is a query
    /// rather than content: a customer's message is already bounded far below this
    /// by the public chat policy, so in practice nothing reaches it. It is here so
    /// that a future channel with a larger message bound cannot turn a long
    /// message into a provider rejection in the middle of a live turn.
    /// </remarks>
    private string Shorten(string query) =>
        query.Length <= _embeddings.Profile.MaxInputCharacters
            ? query
            : query[.._embeddings.Profile.MaxInputCharacters];
}
