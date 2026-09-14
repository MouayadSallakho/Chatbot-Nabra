using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge.Files;

/// <summary>
/// Where a document's text came from, as an administrator reads it.
/// </summary>
/// <remarks>
/// <strong>No fingerprint, and no bytes.</strong> The digest is an internal
/// comparison value; publishing it would let anybody who can read one document
/// test whether a file they hold is the same one, which is a question they have
/// not been given the right to ask. What an administrator needs is the name, the
/// kind, the size and when it was read.
/// </remarks>
public sealed record KnowledgeSourceView(
    string FileName,
    KnowledgeFileType FileType,
    long ByteLength,
    DateTimeOffset ExtractedAt)
{
    internal static KnowledgeSourceView From(KnowledgeSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new KnowledgeSourceView(
            source.FileName.Value,
            source.FileType,
            source.ByteLength,
            source.ExtractedAt);
    }
}
