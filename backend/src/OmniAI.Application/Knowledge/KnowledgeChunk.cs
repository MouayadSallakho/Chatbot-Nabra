namespace OmniAI.Application.Knowledge;

/// <summary>
/// One piece of a document, in the order it appears in it.
/// </summary>
/// <remarks>
/// The ordinal is part of the identity of a chunk within its document, not a
/// display detail: it is what makes an index reproducible, what the unique
/// constraint in the database is built on, and what lets somebody reading a
/// retrieval result see where in the document the answer came from.
/// </remarks>
public sealed record KnowledgeChunk(int Ordinal, string Text);
