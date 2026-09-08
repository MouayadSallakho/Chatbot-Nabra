using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Knowledge;

/// <summary>The small values around the aggregate.</summary>
public sealed class KnowledgeValueTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_title_is_required()
    {
        Assert.Throws<DomainValidationException>(() => KnowledgeTitle.Create("  "));
    }

    [Fact]
    public void A_title_must_be_one_line()
    {
        // A title appears in lists, in checkboxes and beside a search result, all
        // of which assume one line.
        Assert.Throws<DomainValidationException>(() => KnowledgeTitle.Create("First\nSecond"));
    }

    [Fact]
    public void A_title_beyond_the_bound_is_refused()
    {
        Assert.Throws<DomainValidationException>(
            () => KnowledgeTitle.Create(new string('a', KnowledgeTitle.MaxLength + 1)));
    }

    [Fact]
    public void A_fingerprint_compares_by_value_not_by_reference()
    {
        // Without this a document read back from the database would never match
        // the fingerprint of its own content, and every document would report as
        // permanently out of date.
        byte[] bytes = [.. Enumerable.Range(0, ContentFingerprint.Length).Select(index => (byte)index)];

        Assert.Equal(ContentFingerprint.From(bytes), ContentFingerprint.From([.. bytes]));
        Assert.True(ContentFingerprint.From(bytes) == ContentFingerprint.From([.. bytes]));
    }

    [Fact]
    public void A_fingerprint_cannot_be_mutated_after_it_is_made()
    {
        byte[] bytes = new byte[ContentFingerprint.Length];

        ContentFingerprint fingerprint = ContentFingerprint.From(bytes);

        bytes[0] = 0xFF;

        Assert.NotEqual(0xFF, fingerprint.ToArray()[0]);
    }

    [Fact]
    public void A_fingerprint_must_be_the_right_length()
    {
        Assert.Throws<DomainValidationException>(() => ContentFingerprint.From([1, 2, 3]));
        Assert.Throws<DomainValidationException>(() => ContentFingerprint.From(null));
    }

    [Fact]
    public void A_profile_identifier_is_compared_not_interpreted()
    {
        Assert.Equal(
            EmbeddingProfileId.Create("provider:model:1536"),
            EmbeddingProfileId.Create("  provider:model:1536  "));

        Assert.NotEqual(
            EmbeddingProfileId.Create("provider:model:1536"),
            EmbeddingProfileId.Create("provider:model:3072"));
    }

    [Fact]
    public void A_profile_identifier_refuses_whitespace_and_control_characters()
    {
        Assert.Throws<DomainValidationException>(() => EmbeddingProfileId.Create("model 1536"));
        Assert.Throws<DomainValidationException>(() => EmbeddingProfileId.Create("model\n1536"));
        Assert.Throws<DomainValidationException>(() => EmbeddingProfileId.Create(" "));
    }

    [Fact]
    public void An_assignment_needs_a_workspace_an_agent_and_a_document()
    {
        TenantId workspace = TenantId.New();
        AiAgentId agent = AiAgentId.New();
        KnowledgeDocumentId document = KnowledgeDocumentId.New();

        Assert.Throws<DomainValidationException>(
            () => AgentKnowledgeAssignment.Create(default, agent, document, Noon));

        Assert.Throws<DomainValidationException>(
            () => AgentKnowledgeAssignment.Create(workspace, default, document, Noon));

        Assert.Throws<DomainValidationException>(
            () => AgentKnowledgeAssignment.Create(workspace, agent, default, Noon));

        Assert.Throws<DomainValidationException>(
            () => AgentKnowledgeAssignment.Create(workspace, agent, document, default));
    }

    [Fact]
    public void An_agent_records_a_configuration_change_without_learning_what_changed()
    {
        // The version has to move so a second editor cannot silently overwrite the
        // first, and the agent aggregate stays narrow: it gains no knowledge
        // state, only a later update instant.
        AiAgent agent = AiAgent.Create(
            TenantId.New(),
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Be helpful."),
            Noon);

        agent.MarkConfigurationChanged(Noon.AddMinutes(5));

        Assert.Equal(Noon.AddMinutes(5), agent.UpdatedAt);
    }

    [Fact]
    public void A_configuration_change_never_moves_the_instant_backwards()
    {
        AiAgent agent = AiAgent.Create(
            TenantId.New(),
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Be helpful."),
            Noon);

        agent.MarkConfigurationChanged(Noon.AddMinutes(5));
        agent.MarkConfigurationChanged(Noon.AddMinutes(1));

        Assert.Equal(Noon.AddMinutes(5), agent.UpdatedAt);
    }
}
