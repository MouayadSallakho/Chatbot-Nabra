using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;
using OmniAI.Application.Ai.Usage;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// A workspace with agents and documents, in a real database.
/// </summary>
/// <remarks>
/// Nothing here is substituted except the embedding provider, which is a
/// deterministic local function rather than a network call: the vectors under
/// test have to be ones the test chose, so that a cosine ordering assertion is
/// about pgvector rather than about what a model happened to think.
/// </remarks>
internal sealed class KnowledgeWorld(PostgresTestDatabase database)
{
    public const int Dimensions = 1_536;

    /// <summary>The profile these fixtures index under.</summary>
    public static readonly EmbeddingProfileId Profile =
        EmbeddingProfileId.Create("test:deterministic:1536");

    private readonly PostgresTestDatabase _database = database;

    public OmniAiDbContext Context() => _database.CreateContext();

    /// <summary>A workspace with a real owner account, so every foreign key holds.</summary>
    /// <remarks>
    /// The account is a real Identity row rather than an invented id. A membership
    /// pointing at a user that does not exist is refused by the database, which is
    /// the schema working -- and seeding around it would mean these fixtures wrote
    /// rows a deployment never could.
    /// </remarks>
    public async Task<TenantId> WorkspaceAsync(string name = "Acme Trading")
    {
        UserId owner = await Persistence.TestIdentity.SeedUserAsync(_database);

        NewWorkspace workspace = NewWorkspace.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]),
            owner,
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = Context();

        context.Tenants.Add(workspace.Tenant);
        context.TenantMemberships.Add(workspace.Membership);
        context.TenantRoles.Add(workspace.OwnerRole);
        context.TenantRoleAssignments.Add(workspace.OwnerAssignment);

        context.Entry(workspace.OwnerRole)
            .Property<string>("NormalisedName")
            .CurrentValue = workspace.OwnerRole.Name.Normalised;

        await context.SaveChangesAsync();

        return workspace.Tenant.Id;
    }

    public async Task<AiAgent> AgentAsync(TenantId workspace, string name = "Support")
    {
        AiAgent agent = AiAgent.Create(
            workspace,
            AiAgentName.Create(name),
            AgentInstructions.Create("Be helpful."),
            DateTimeOffset.UtcNow);

        agent.Activate(DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = Context();

        context.AiAgents.Add(agent);
        await context.SaveChangesAsync();

        return agent;
    }

    /// <summary>A document, written through the real repository.</summary>
    public async Task<Versioned<KnowledgeDocument>> DocumentAsync(
        TenantId workspace,
        string title = "Returns policy",
        string content = "Goods may be returned within 30 days.")
    {
        await using OmniAiDbContext context = Context();

        KnowledgeDocumentRepository repository = new(context, Embeddings());

        KnowledgeDocument document = KnowledgeDocument.Create(
            workspace,
            KnowledgeTitle.Create(title),
            KnowledgeContent.Create(content),
            DateTimeOffset.UtcNow);

        ConcurrencyToken version = await repository.AddAsync(document);

        return new Versioned<KnowledgeDocument>(document, version);
    }

    /// <summary>Assigns a document to an agent, through the real repository.</summary>
    public async Task AssignAsync(AiAgent agent, params KnowledgeDocumentId[] documentIds)
    {
        await using OmniAiDbContext context = Context();

        AiAgent tracked = await context.AiAgents.SingleAsync(
            candidate => candidate.Id == agent.Id && candidate.TenantId == agent.TenantId);

        AgentKnowledgeRepository repository = new(context);

        ConcurrencyToken version = RowVersionOf(context, tracked);

        tracked.MarkConfigurationChanged(DateTimeOffset.UtcNow);

        await repository.ReplaceAsync(tracked, version, documentIds, DateTimeOffset.UtcNow);
    }

    /// <summary>Indexes a document with vectors the caller chose.</summary>
    /// <remarks>
    /// The vectors are supplied rather than generated so an ordering assertion is
    /// about the database's arithmetic and not about a model's opinion.
    /// </remarks>
    public async Task<ConcurrencyToken> IndexAsync(
        KnowledgeDocumentId documentId,
        TenantId workspace,
        ConcurrencyToken expected,
        params (string Text, float[] Vector)[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        await using OmniAiDbContext context = Context();

        KnowledgeDocumentRepository documents = new(context, Embeddings());

        Versioned<KnowledgeDocument> stored =
            await documents.FindAsync(workspace, documentId)
            ?? throw new InvalidOperationException("That document does not exist.");

        stored.Value.MarkIndexed(Profile, DateTimeOffset.UtcNow);

        KnowledgeChunkWrite[] writes =
        [
            .. chunks.Select((chunk, ordinal) => new KnowledgeChunkWrite(
                ordinal,
                chunk.Text,
                EmbeddingVector.Create(chunk.Vector, Dimensions))),
        ];

        KnowledgeIndexStore store = new(context);

        return await store.ReplaceIndexAsync(stored.Value, expected, writes);
    }

    public static KnowledgeSearchStore Search(OmniAiDbContext context) => new(context);

    /// <summary>How many chunks a document currently has.</summary>
    public async Task<long> ChunkCountAsync(KnowledgeDocumentId documentId)
    {
        object? count = await _database.ExecuteScalarAsync(
            $"SELECT count(*) FROM knowledge_chunks WHERE knowledge_document_id = '{documentId}'");

        return (long)(count ?? 0L);
    }

    /// <summary>The chunk texts a document holds, in ordinal order.</summary>
    public async Task<IReadOnlyList<string>> ChunkTextsAsync(KnowledgeDocumentId documentId)
    {
        await using Npgsql.NpgsqlConnection connection = new(_database.ConnectionString);
        await connection.OpenAsync();

        await using Npgsql.NpgsqlCommand command = new(
            "SELECT content FROM knowledge_chunks WHERE knowledge_document_id = @id ORDER BY ordinal",
            connection);

        command.Parameters.AddWithValue("id", documentId.Value);

        List<string> texts = [];

        await using Npgsql.NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            texts.Add(reader.GetString(0));
        }

        return texts;
    }

    /// <summary>
    /// A unit vector pointing mostly along one axis.
    /// </summary>
    /// <remarks>
    /// Deliberately simple. Cosine distance between two of these is a number that
    /// can be worked out by hand, so an ordering assertion is meaningful rather
    /// than merely reproducible.
    /// </remarks>
    public static float[] Axis(int axis, float magnitude = 1f)
    {
        float[] values = new float[Dimensions];

        values[axis] = magnitude;

        return values;
    }

    /// <summary>A vector between two axes, closer to the first.</summary>
    public static float[] Between(int first, int second, float weight)
    {
        float[] values = new float[Dimensions];

        values[first] = weight;
        values[second] = 1f - weight;

        return values;
    }

    /// <summary>An embedding generator that never touches a network.</summary>
    public static ITextEmbeddingGenerator Embeddings() => new FixedEmbeddingGenerator();

    /// <summary>The version an agent row is currently at.</summary>
    private static ConcurrencyToken RowVersionOf(OmniAiDbContext context, AiAgent agent) =>
        RowVersionTokens.Encode(context.Entry(agent).Property<uint>("xmin").CurrentValue);

    /// <summary>
    /// A generator with a real profile and no provider behind it.
    /// </summary>
    /// <remarks>
    /// Used where a repository needs a profile in order to report readiness. It
    /// refuses to embed anything, which is correct here: these suites choose their
    /// own vectors, and a store test that quietly generated one would be testing
    /// the wrong thing.
    /// </remarks>
    private sealed class FixedEmbeddingGenerator : ITextEmbeddingGenerator
    {
        public EmbeddingProfile Profile { get; } = EmbeddingProfile.Create(
            KnowledgeWorld.Profile,
            Dimensions,
            maxBatchSize: 32,
            maxInputCharacters: 8_000);

        public Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
            IReadOnlyList<string> inputs,
            AiUsageContext usage,
            CancellationToken cancellationToken = default) =>
            throw new EmbeddingUnavailableException(
                "This fixture chooses its own vectors and never embeds anything.");
    }
}
