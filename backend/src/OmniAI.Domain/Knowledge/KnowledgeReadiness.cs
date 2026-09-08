namespace OmniAI.Domain.Knowledge;

/// <summary>
/// Whether a document can currently answer anything.
/// </summary>
/// <remarks>
/// Three states rather than a pair of booleans, because the three mean genuinely
/// different things to the person looking at the screen and lead to different
/// actions: switch it back on, index it, or nothing at all.
/// </remarks>
public enum KnowledgeReadiness
{
    /// <summary>
    /// Switched off. Never retrieved, whatever its index says.
    /// </summary>
    /// <remarks>
    /// Deliberately ranked first: a disabled document that also needs indexing
    /// reports as disabled, because that is the thing standing in the way.
    /// </remarks>
    Disabled = 0,

    /// <summary>
    /// Enabled, but the current text has no compatible index behind it.
    /// </summary>
    /// <remarks>
    /// Never retrieved. This is the state a brand new document starts in, and the
    /// state any edit to the content returns it to.
    /// </remarks>
    NeedsIndex = 1,

    /// <summary>
    /// Enabled, and the text as it stands right now is indexed under the profile
    /// in use.
    /// </summary>
    Ready = 2,
}
