using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OmniAI.Domain;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// Cuts a document into the pieces that get embedded.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A pure function, and it has to be.</strong> The same text must always
/// produce exactly the same chunks, in the same order, on any machine and in any
/// culture. Everything downstream depends on it: re-indexing unchanged content is
/// recognised as unnecessary by comparing a fingerprint, which is only sound if
/// identical text would have produced an identical index. Nothing here reads a
/// clock, a random number, a configuration value or a locale.
/// </para>
/// <para>
/// <strong>No AI is involved in chunking, deliberately.</strong> Asking a model
/// where to cut would make the boundaries non-reproducible, would cost money on
/// every save, and would make the index depend on which model answered that day.
/// The rules below are boring on purpose.
/// </para>
/// <para>
/// <strong>The rules, in order.</strong>
/// </para>
/// <list type="number">
/// <item>
/// The text is split into paragraphs on runs of two or more line feeds. A blank
/// line is the one boundary every writer already uses on purpose, so it is the
/// one worth respecting.
/// </item>
/// <item>
/// Consecutive paragraphs are packed together, joined by a blank line, while they
/// fit within <see cref="KnowledgeChunkingPolicy.MaxChunkCharacters"/>. Packing
/// matters: a document written as twenty short paragraphs would otherwise become
/// twenty tiny embeddings, each too small to carry the context that makes it
/// findable.
/// </item>
/// <item>
/// A single paragraph longer than the bound is cut into overlapping windows. The
/// cut is made at the last whitespace within
/// <see cref="KnowledgeChunkingPolicy.OverlapCharacters"/> of the limit so a word
/// is not split down the middle, and falls back to an exact character position
/// when a paragraph contains no whitespace at all. Each window after the first
/// starts <see cref="KnowledgeChunkingPolicy.OverlapCharacters"/> characters
/// before the cut, so a sentence that straddles a boundary appears whole in one
/// of the two.
/// </item>
/// <item>
/// Chunks that fall on a paragraph boundary carry no overlap. The blank line is a
/// real boundary the author wrote, and duplicating text across it would pay to
/// embed the same words twice and would return the same passage as two separate
/// results.
/// </item>
/// </list>
/// <para>
/// Nothing is truncated: every character of the document ends up in at least one
/// chunk. A document that would somehow exceed
/// <see cref="KnowledgeChunkingPolicy.MaxChunks"/> is refused rather than cut
/// short.
/// </para>
/// </remarks>
public sealed partial class KnowledgeChunker(KnowledgeChunkingPolicy? policy = null)
{
    private readonly KnowledgeChunkingPolicy _policy = policy ?? new KnowledgeChunkingPolicy();

    /// <summary>Splits normalised document text into chunks.</summary>
    /// <exception cref="DomainValidationException">
    /// The text would produce more chunks than the policy allows.
    /// </exception>
    public IReadOnlyList<KnowledgeChunk> Split(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        List<string> pieces = [];
        StringBuilder packed = new();

        foreach (string paragraph in ParagraphBreak().Split(content))
        {
            string trimmed = paragraph.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Length > _policy.MaxChunkCharacters)
            {
                // An over-long paragraph cannot join anything: flush whatever was
                // accumulating, then window it on its own.
                Flush(pieces, packed);

                foreach (string window in Window(trimmed))
                {
                    pieces.Add(window);
                }

                continue;
            }

            // +2 for the blank line that will join them.
            int joined = packed.Length == 0 ? trimmed.Length : packed.Length + 2 + trimmed.Length;

            if (joined > _policy.MaxChunkCharacters)
            {
                Flush(pieces, packed);
            }

            if (packed.Length > 0)
            {
                packed.Append("\n\n");
            }

            packed.Append(trimmed);
        }

        Flush(pieces, packed);

        if (pieces.Count > _policy.MaxChunks)
        {
            throw new DomainValidationException(string.Create(
                CultureInfo.InvariantCulture,
                $"This document would be split into {pieces.Count} pieces, and at most {_policy.MaxChunks} are allowed. Nothing was indexed and nothing was truncated. Split it into separate documents instead."))
            {
                Code = "knowledge.index.too_many_chunks",
            };
        }

        KnowledgeChunk[] chunks = new KnowledgeChunk[pieces.Count];

        for (int index = 0; index < pieces.Count; index++)
        {
            chunks[index] = new KnowledgeChunk(index, pieces[index]);
        }

        return chunks;
    }

    private static void Flush(List<string> pieces, StringBuilder packed)
    {
        if (packed.Length == 0)
        {
            return;
        }

        pieces.Add(packed.ToString());
        packed.Clear();
    }

    /// <summary>Cuts one over-long paragraph into overlapping windows.</summary>
    private IEnumerable<string> Window(string paragraph)
    {
        int max = _policy.MaxChunkCharacters;
        int overlap = _policy.OverlapCharacters;
        int start = 0;

        while (start < paragraph.Length)
        {
            if (paragraph.Length - start <= max)
            {
                string tail = paragraph[start..].Trim();

                if (tail.Length > 0)
                {
                    yield return tail;
                }

                yield break;
            }

            int limit = start + max;
            int cut = LastBreak(paragraph, Math.Max(start + 1, limit - overlap), limit);

            if (cut <= start)
            {
                // No whitespace anywhere in the lookback window: a run of
                // unbroken characters, which does happen -- a base64 blob pasted
                // into a policy document, a language written without spaces. Cut
                // at the exact position rather than growing the window until
                // something breaks, which for such a document would never happen.
                cut = limit;
            }

            string window = paragraph[start..cut].Trim();

            if (window.Length > 0)
            {
                yield return window;
            }

            int next = cut - overlap;

            // The lookback is bounded by the overlap, so this always advances by
            // at least (max - 2 * overlap) characters. The guard is here so the
            // loop cannot spin even if those numbers are ever reconfigured badly.
            start = next > start ? next : cut;
        }
    }

    /// <summary>The last whitespace position in <c>[from, to)</c>, or -1.</summary>
    private static int LastBreak(string text, int from, int to)
    {
        for (int index = to - 1; index >= from; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// A run of two or more line feeds: one blank line or more.
    /// </summary>
    /// <remarks>
    /// Only line feeds, because content arrives with its line endings already
    /// folded to a single form. A pattern that also matched carriage returns
    /// would suggest they can still be here, which would be misleading.
    /// </remarks>
    [GeneratedRegex(@"\n{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphBreak();
}
