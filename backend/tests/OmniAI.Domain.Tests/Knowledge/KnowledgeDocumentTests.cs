using OmniAI.Domain;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Knowledge;

/// <summary>
/// The knowledge document aggregate: what it is, and what it refuses.
/// </summary>
public sealed class KnowledgeDocumentTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId Profile =
        EmbeddingProfileId.Create("provider:model:1536");

    private static readonly EmbeddingProfileId OtherProfile =
        EmbeddingProfileId.Create("provider:other-model:1536");

    private static KnowledgeDocument Document(string content = "Goods may be returned in 30 days.") =>
        KnowledgeDocument.Create(
            TenantId.New(),
            KnowledgeTitle.Create("Returns policy"),
            KnowledgeContent.Create(content),
            Noon);

    [Fact]
    public void A_new_document_is_enabled_but_not_ready()
    {
        // There is text but nothing has been indexed. It can be assigned to an
        // agent immediately and simply will not answer anything until somebody
        // pays to index it, which is the honest default in both directions.
        KnowledgeDocument document = Document();

        Assert.True(document.IsEnabled);
        Assert.Equal(KnowledgeReadiness.NeedsIndex, document.ReadinessFor(Profile));
        Assert.Null(document.IndexedAt);
        Assert.Null(document.IndexedFingerprint);
        Assert.Null(document.IndexedProfile);
    }

    [Fact]
    public void Indexing_makes_it_ready()
    {
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        Assert.Equal(KnowledgeReadiness.Ready, document.ReadinessFor(Profile));
        Assert.Equal(Noon.AddMinutes(1), document.IndexedAt);
        Assert.True(document.IsIndexCurrent(Profile));
    }

    [Fact]
    public void Editing_the_content_takes_readiness_away()
    {
        // The single most important rule in the aggregate. Vectors describe text;
        // change the text and they describe something nobody can see any more.
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        document.Revise(
            KnowledgeTitle.Create("Returns policy"),
            KnowledgeContent.Create("Goods may be returned in 14 days."),
            Noon.AddMinutes(2));

        Assert.Equal(KnowledgeReadiness.NeedsIndex, document.ReadinessFor(Profile));
        Assert.False(document.IsIndexCurrent(Profile));
    }

    [Fact]
    public void Editing_only_the_title_leaves_it_ready()
    {
        // Renaming a document does not make its vectors wrong, and charging
        // somebody for a re-index because they fixed a heading would be an
        // invented cost.
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        document.Revise(
            KnowledgeTitle.Create("Returns and refunds"),
            document.Content,
            Noon.AddMinutes(2));

        Assert.Equal(KnowledgeReadiness.Ready, document.ReadinessFor(Profile));
    }

    [Fact]
    public void Restoring_the_original_text_makes_it_ready_again()
    {
        // Because readiness is derived from a fingerprint rather than stored, an
        // edit that is undone costs nothing: the fingerprint returns to what was
        // indexed and the old chunks describe the text again.
        KnowledgeDocument document = Document("Original.");

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        document.Revise(document.Title, KnowledgeContent.Create("Changed."), Noon.AddMinutes(2));

        Assert.Equal(KnowledgeReadiness.NeedsIndex, document.ReadinessFor(Profile));

        document.Revise(document.Title, KnowledgeContent.Create("Original."), Noon.AddMinutes(3));

        Assert.Equal(KnowledgeReadiness.Ready, document.ReadinessFor(Profile));
    }

    [Fact]
    public void An_index_made_under_a_different_profile_does_not_count()
    {
        // Distance between vectors from two different models is a perfectly
        // well-formed number and a completely meaningless one. Without this the
        // change would be invisible: retrieval would keep working and would keep
        // returning confidently ranked nonsense.
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        Assert.Equal(KnowledgeReadiness.NeedsIndex, document.ReadinessFor(OtherProfile));
        Assert.False(document.IsIndexCurrent(OtherProfile));
    }

    [Fact]
    public void A_disabled_document_is_never_ready()
    {
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));
        document.Disable(Noon.AddMinutes(2));

        Assert.Equal(KnowledgeReadiness.Disabled, document.ReadinessFor(Profile));
    }

    [Fact]
    public void Disabling_keeps_the_index_so_enabling_costs_nothing()
    {
        // Switching a document off is what somebody reaches for when a policy has
        // just changed. Throwing away the index they paid for would punish them
        // for acting quickly.
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));
        document.Disable(Noon.AddMinutes(2));
        document.Enable(Noon.AddMinutes(3));

        Assert.Equal(KnowledgeReadiness.Ready, document.ReadinessFor(Profile));
    }

    [Fact]
    public void Enabling_an_enabled_document_is_refused()
    {
        Assert.Throws<DomainStateException>(() => Document().Enable(Noon.AddMinutes(1)));
    }

    [Fact]
    public void Disabling_a_disabled_document_is_refused()
    {
        KnowledgeDocument document = Document();

        document.Disable(Noon.AddMinutes(1));

        Assert.Throws<DomainStateException>(() => document.Disable(Noon.AddMinutes(2)));
    }

    [Fact]
    public void The_fingerprint_recorded_is_the_content_the_document_holds()
    {
        // MarkIndexed takes no fingerprint from its caller, so an indexer cannot
        // report success for text this document does not have.
        KnowledgeDocument document = Document();

        document.MarkIndexed(Profile, Noon.AddMinutes(1));

        Assert.Equal(document.Fingerprint, document.IndexedFingerprint);
    }

    [Fact]
    public void A_document_must_belong_to_a_workspace()
    {
        Assert.Throws<DomainValidationException>(() => KnowledgeDocument.Create(
            default,
            KnowledgeTitle.Create("Returns policy"),
            KnowledgeContent.Create("Text."),
            Noon));
    }

    [Fact]
    public void An_update_cannot_be_dated_before_the_last_one()
    {
        KnowledgeDocument document = Document();

        document.Revise(document.Title, document.Content, Noon.AddMinutes(5));

        Assert.Throws<DomainValidationException>(() =>
            document.Revise(document.Title, document.Content, Noon.AddMinutes(1)));
    }

    [Fact]
    public void The_workspace_cannot_be_changed()
    {
        // Not an assertion about behaviour but about surface: there is no
        // operation that moves a document between workspaces, because there is no
        // safe version of one -- the chunks, the vectors and every assignment are
        // scoped by it.
        Assert.DoesNotContain(
            typeof(KnowledgeDocument).GetMethods(),
            method => method.Name.Contains("Tenant", StringComparison.Ordinal)
                && method.Name.StartsWith("set_", StringComparison.Ordinal));
    }

    [Fact]
    public void The_aggregate_names_no_vector_no_provider_and_no_persistence_type()
    {
        // The boundary this aggregate exists to hold. A vector, an embedding, a
        // chunk or an EF type appearing on it would mean the business record had
        // been fused to the machinery that indexes it.
        string[] forbidden =
        [
            "Vector", "Embedding", "Chunk", "OpenAi", "OpenAI", "DbContext", "Entity", "Npgsql",
        ];

        IEnumerable<string> names = typeof(KnowledgeDocument)
            .GetMembers()
            .Select(member => member.Name)
            .Concat(typeof(KnowledgeDocument)
                .GetProperties()
                .Select(property => property.PropertyType.FullName ?? string.Empty));

        foreach (string name in names)
        {
            foreach (string word in forbidden)
            {
                // EmbeddingProfileId is the one deliberate exception: it is an
                // opaque marker the domain compares and never reads, and it is
                // what makes an incompatible index detectable at all.
                if (name.Contains("EmbeddingProfileId", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.DoesNotContain(word, name, StringComparison.Ordinal);
            }
        }
    }
}
