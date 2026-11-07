using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.SearchAgentKnowledge;
using OmniAI.Application.Tests.AiAgents;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Phase 37.6, row 30: the admin retrieval tester's embedding is recorded as
/// <see cref="AiUsagePurpose.AgentRetrievalTest"/>, never as a reply's.
/// </summary>
public sealed class RetrievalTesterPurposeTests
{
    [Fact]
    public async Task The_tester_records_its_question_under_its_own_purpose()
    {
        Guid workspace = Guid.CreateVersion7();
        AiAgent agent = AiAgent.Create(
            TenantId.From(workspace),
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Answer briefly."),
            DateTimeOffset.UtcNow);

        FakeKnowledgeSearchStore store = new();
        store.AddRun(agent.TenantId, agent.Id, count: 2, textLength: 120);
        RecordingEmbeddingGenerator embeddings = new();

        SearchAgentKnowledgeHandler handler = new(
            new InMemoryAiAgentRepository().Seed(agent),
            new KnowledgeRetriever(store, embeddings, new KnowledgeRetrievalPolicy()),
            TestAuthority.Owner(UserId.New(), workspace));

        await handler.HandleAsync(new SearchAgentKnowledgeQuery(workspace, agent.Id.Value, "Do you deliver?"));

        AiUsageContext usage = Assert.Single(embeddings.Usages);
        Assert.Equal(AiUsagePurpose.AgentRetrievalTest, usage.Purpose);
        Assert.Equal(agent.TenantId, usage.TenantId);
    }
}
