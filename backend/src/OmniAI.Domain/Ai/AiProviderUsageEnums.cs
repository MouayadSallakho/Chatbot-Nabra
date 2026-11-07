namespace OmniAI.Domain.Ai;

/// <summary>
/// Which company was paid.
/// </summary>
/// <remarks>
/// An enum rather than a string, because a workspace's usage is grouped by it
/// and a typo would silently split one provider's spend into two columns that
/// each look plausible.
/// </remarks>
public enum AiProvider
{
    /// <summary>The only provider this platform calls today.</summary>
    OpenAi = 1,

    /// <summary>
    /// The in-process stand-in a developer machine may opt into (Phase 37.2).
    /// </summary>
    /// <remarks>
    /// Nobody is paid. It has its own value rather than borrowing
    /// <see cref="OpenAi"/>, so a local row can never be mistaken for real spend,
    /// and a deployment outside Development cannot produce one: the API refuses to
    /// start there with the stand-in requested.
    /// </remarks>
    DevelopmentStandIn = 2,
}

/// <summary>
/// What kind of provider operation this was.
/// </summary>
/// <remarks>
/// <strong>Generation and embedding are different products with different
/// prices.</strong> Treating an embedding request as generation because both
/// happen to go to OpenAI would make every per-model cost wrong and every
/// "how much of our spend is retrieval" question unanswerable.
/// </remarks>
public enum AiOperationType
{
    /// <summary>A request that produced text.</summary>
    TextGeneration = 1,

    /// <summary>A request that produced vectors.</summary>
    Embedding = 2,
}

/// <summary>
/// Why the platform spent this money.
/// </summary>
/// <remarks>
/// <strong>A closed set, and never a string somebody typed.</strong> A
/// free-form purpose would be a second place for customer content to leak into
/// accounting, and it would make grouping meaningless within a month. Each value
/// here corresponds to a real flow that exists today; a new purpose arrives with
/// the flow that needs it.
/// </remarks>
public enum AiUsagePurpose
{
    /// <summary>Answering a customer in a live conversation.</summary>
    ConversationGeneration = 1,

    /// <summary>Embedding a customer's question so knowledge can be retrieved for it.</summary>
    ConversationRetrievalEmbedding = 2,

    /// <summary>Embedding a knowledge document so it can be searched.</summary>
    KnowledgeIndexEmbedding = 3,

    /// <summary>An administrator trying an agent out from the admin application.</summary>
    AgentPreviewGeneration = 4,

    /// <summary>
    /// An administrator testing what an agent's knowledge retrieves for a question
    /// (Phase 37.6).
    /// </summary>
    /// <remarks>
    /// Before 37.6 the tester's embeddings were recorded as
    /// <see cref="ConversationRetrievalEmbedding"/>. Those rows cannot be told apart
    /// from a live reply's and stay counted there; there is no backfill.
    /// </remarks>
    AgentRetrievalTest = 5,
}

/// <summary>
/// How a provider operation ended.
/// </summary>
/// <remarks>
/// <para>
/// <strong><see cref="Started"/> is written before the provider is called</strong>,
/// so money can never be spent without a record of it. A row still in this state
/// long after the fact is evidence of a process that died mid-flight, which is
/// exactly what somebody reconciling an invoice needs to see.
/// </para>
/// <para>
/// <strong><see cref="Unknown"/> is not a failure and is not a zero.</strong> A
/// connection lost after dispatch may well have been executed and billed by the
/// provider. Recording it as failed would understate spend; recording zero
/// tokens would be an invention. It is reported as what it is: unresolved.
/// </para>
/// </remarks>
public enum AiOperationStatus
{
    /// <summary>Recorded before dispatch. Not yet resolved.</summary>
    Started = 1,

    /// <summary>The provider answered and reported usage.</summary>
    Succeeded = 2,

    /// <summary>The provider definitively refused. Nothing was produced.</summary>
    Failed = 3,

    /// <summary>Dispatched, outcome unknown. It may have been executed and billed.</summary>
    Unknown = 4,
}
