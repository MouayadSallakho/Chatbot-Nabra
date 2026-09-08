using OmniAI.Application.Knowledge;
using OmniAI.Domain;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// The chunker: deterministic, paragraph-aware, and never truncating.
/// </summary>
public sealed class KnowledgeChunkerTests
{
    /// <summary>
    /// Small numbers, so a boundary can be reached with a readable document.
    /// </summary>
    /// <remarks>
    /// The shape of the rules is what is under test, not the production constants
    /// -- reaching those honestly would need a thousand-word fixture per case, and
    /// nobody would be able to see what had gone wrong when one failed.
    /// </remarks>
    private static readonly KnowledgeChunkingPolicy Small = new()
    {
        MaxChunkCharacters = 100,
        OverlapCharacters = 20,
        MaxChunks = 50,
    };

    private static KnowledgeChunker Chunker(KnowledgeChunkingPolicy? policy = null) =>
        new(policy ?? Small);

    [Fact]
    public void The_same_text_always_produces_the_same_chunks()
    {
        // The property everything else rests on. Re-indexing unchanged content is
        // recognised as unnecessary by comparing a fingerprint, which is only
        // sound if identical text produces an identical index.
        string text = KnowledgeSamples.Words(700);

        IReadOnlyList<KnowledgeChunk> first = Chunker().Split(text);
        IReadOnlyList<KnowledgeChunk> second = Chunker().Split(text);

        Assert.Equal(
            first.Select(chunk => (chunk.Ordinal, chunk.Text)),
            second.Select(chunk => (chunk.Ordinal, chunk.Text)));
    }

    [Fact]
    public void Ordinals_run_from_zero_without_gaps()
    {
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(KnowledgeSamples.Words(700));

        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Ordinal));
    }

    [Fact]
    public void A_short_document_is_one_chunk()
    {
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split("We are open 9 to 5.");

        KnowledgeChunk only = Assert.Single(chunks);

        Assert.Equal("We are open 9 to 5.", only.Text);
    }

    [Fact]
    public void Short_paragraphs_are_packed_together()
    {
        // Twenty short paragraphs would otherwise become twenty tiny embeddings,
        // each too small to carry the context that makes it findable.
        IReadOnlyList<KnowledgeChunk> chunks =
            Chunker().Split("First point.\n\nSecond point.\n\nThird point.");

        KnowledgeChunk only = Assert.Single(chunks);

        Assert.Equal("First point.\n\nSecond point.\n\nThird point.", only.Text);
    }

    [Fact]
    public void Packing_stops_at_the_bound()
    {
        string paragraph = new('a', 60);

        IReadOnlyList<KnowledgeChunk> chunks =
            Chunker().Split($"{paragraph}\n\n{paragraph}\n\n{paragraph}");

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= Small.MaxChunkCharacters));
    }

    [Fact]
    public void No_chunk_exceeds_the_bound()
    {
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(KnowledgeSamples.Words(2_000));

        Assert.All(chunks, chunk => Assert.True(
            chunk.Text.Length <= Small.MaxChunkCharacters,
            $"A chunk of {chunk.Text.Length} characters exceeded {Small.MaxChunkCharacters}."));
    }

    [Fact]
    public void An_over_long_paragraph_is_windowed_with_overlap()
    {
        // A sentence straddling a cut has to survive whole in one of the two
        // pieces, or a question about it matches neither.
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(KnowledgeSamples.Words(400));

        Assert.True(chunks.Count > 1);

        for (int index = 1; index < chunks.Count; index++)
        {
            string previous = chunks[index - 1].Text;
            string current = chunks[index].Text;

            // The tail of one window reappears at the head of the next. Compared
            // on a shortened slice because both ends are trimmed to word
            // boundaries, which moves the exact position by a few characters.
            string tail = previous[^Math.Min(10, previous.Length)..];

            Assert.Contains(tail, current, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Paragraph_boundaries_carry_no_overlap()
    {
        // A blank line is a boundary the author wrote. Duplicating across it would
        // pay to embed the same words twice and return one passage as two results.
        string first = new('a', 90);
        string second = new('b', 90);

        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split($"{first}\n\n{second}");

        Assert.Equal(2, chunks.Count);
        Assert.Equal(first, chunks[0].Text);
        Assert.Equal(second, chunks[1].Text);
    }

    [Fact]
    public void Every_character_of_the_document_survives_somewhere()
    {
        // Nothing is truncated. The concatenation of the chunks contains every
        // word of the original, in order.
        string text = KnowledgeSamples.Words(600);

        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(text);

        string joined = string.Concat(chunks.Select(chunk => chunk.Text));

        foreach (string word in text.Split(' '))
        {
            Assert.Contains(word, joined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_paragraph_with_no_whitespace_is_cut_at_an_exact_position()
    {
        // A base64 blob pasted into a policy document, or a language written
        // without spaces. Growing the window until something breaks would, for
        // such a document, never terminate.
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(new string('x', 350));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= Small.MaxChunkCharacters));
    }

    [Fact]
    public void Runs_of_blank_lines_are_one_boundary()
    {
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split("First.\n\n\n\n\nSecond.");

        KnowledgeChunk only = Assert.Single(chunks);

        Assert.Equal("First.\n\nSecond.", only.Text);
    }

    [Fact]
    public void No_chunk_is_blank_or_padded()
    {
        IReadOnlyList<KnowledgeChunk> chunks = Chunker().Split(KnowledgeSamples.Words(500));

        Assert.All(chunks, chunk =>
        {
            Assert.NotEqual(0, chunk.Text.Length);
            Assert.Equal(chunk.Text.Trim(), chunk.Text);
        });
    }

    [Fact]
    public void Blank_content_produces_nothing_rather_than_an_empty_chunk()
    {
        Assert.Empty(Chunker().Split("   "));
        Assert.Empty(Chunker().Split(null));
    }

    [Fact]
    public void A_document_that_would_exceed_the_chunk_bound_is_refused()
    {
        KnowledgeChunker chunker = Chunker(new KnowledgeChunkingPolicy
        {
            MaxChunkCharacters = 20,
            OverlapCharacters = 4,
            MaxChunks = 3,
        });

        DomainValidationException failure = Assert.Throws<DomainValidationException>(
            () => chunker.Split(KnowledgeSamples.Words(400)));

        Assert.Contains("nothing was truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_production_policy_keeps_a_full_document_within_its_bounds()
    {
        // The real numbers, against the largest document the content bound allows.
        // This is what proves the backstop is a backstop rather than a limit
        // somebody will meet.
        KnowledgeChunker chunker = new();

        IReadOnlyList<KnowledgeChunk> chunks = chunker.Split(KnowledgeSamples.Words(100_000));

        Assert.True(chunks.Count > 60, $"Expected a real split; got {chunks.Count} chunks.");
        Assert.True(chunks.Count < 200, $"Expected far fewer than the backstop; got {chunks.Count}.");
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= 1_500));
    }
}
