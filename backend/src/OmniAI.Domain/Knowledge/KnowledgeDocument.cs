using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// A piece of written business information a workspace wants its AI agents to be
/// able to draw on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a document, not an index.</strong> No vector, no chunk, no
/// embedding, no provider type and no persistence type appears here, and none may
/// be added. What this aggregate owns is the text a person wrote and the question
/// "is what is currently indexed still this text?" -- the index itself lives
/// entirely below, and could be thrown away and rebuilt without changing a single
/// field on this type.
/// </para>
/// <para>
/// <strong>Readiness is derived, never stored.</strong> There is no
/// <c>Status = Ready</c> to get out of step with reality. A document is ready
/// when it is enabled and the fingerprint of the text it holds right now equals
/// the fingerprint that was indexed, under a compatible profile. That makes the
/// dangerous state -- a document claiming to be ready while the chunks behind it
/// describe an older draft -- unrepresentable rather than merely avoided.
/// </para>
/// <para>
/// <strong>Indexing is explicit and costs money.</strong> Nothing here indexes
/// anything; saving text does not spend a penny. The aggregate records that an
/// index happened, and derives the fingerprint it records from the content it is
/// actually holding.
/// </para>
/// </remarks>
public sealed class KnowledgeDocument
{
    private KnowledgeDocument(
        KnowledgeDocumentId id,
        TenantId tenantId,
        KnowledgeTitle title,
        KnowledgeContent content,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Title = title;
        Content = content;
        IsEnabled = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Identity. Fixed for the lifetime of the document.</summary>
    public KnowledgeDocumentId Id { get; }

    /// <summary>
    /// The workspace that owns it.
    /// </summary>
    /// <remarks>
    /// Read-only and set once. There is no operation that moves a document
    /// between workspaces, because there is no safe version of one: the chunks,
    /// the vectors and every agent assignment are all scoped by this value.
    /// </remarks>
    public TenantId TenantId { get; }

    public KnowledgeTitle Title { get; private set; }

    public KnowledgeContent Content { get; private set; }

    /// <summary>
    /// Whether the workspace wants this document used at all.
    /// </summary>
    /// <remarks>
    /// Separate from readiness on purpose. Switching a document off is how
    /// somebody stops an out-of-date policy being quoted at customers right now,
    /// without deleting the text or throwing away the index they paid for.
    /// </remarks>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The fingerprint of the content that was last successfully indexed, or
    /// <c>null</c> if the document has never been indexed.
    /// </summary>
    public ContentFingerprint? IndexedFingerprint { get; private set; }

    /// <summary>The profile the stored vectors were produced under.</summary>
    public EmbeddingProfileId? IndexedProfile { get; private set; }

    /// <summary>When the index behind this document was last rebuilt.</summary>
    public DateTimeOffset? IndexedAt { get; private set; }

    /// <summary>The fingerprint of the text this document holds right now.</summary>
    public ContentFingerprint Fingerprint => Content.Fingerprint();

    /// <summary>
    /// Whether the stored index describes this exact text, under this profile.
    /// </summary>
    public bool IsIndexCurrent(EmbeddingProfileId profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return IndexedFingerprint is not null
            && IndexedProfile is not null
            && IndexedFingerprint == Fingerprint
            && IndexedProfile == profile;
    }

    /// <summary>What this document can do, under the profile currently in use.</summary>
    public KnowledgeReadiness ReadinessFor(EmbeddingProfileId profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!IsEnabled)
        {
            return KnowledgeReadiness.Disabled;
        }

        return IsIndexCurrent(profile) ? KnowledgeReadiness.Ready : KnowledgeReadiness.NeedsIndex;
    }

    /// <summary>
    /// Writes a new document.
    /// </summary>
    /// <remarks>
    /// Enabled from the start and not ready: there is text but nothing has been
    /// indexed, so it can be assigned to an agent and will simply not be
    /// retrieved until somebody indexes it. Creating a document that silently
    /// spent money on embeddings would be the wrong default in both directions --
    /// cost and surprise.
    /// </remarks>
    public static KnowledgeDocument Create(
        TenantId tenantId,
        KnowledgeTitle title,
        KnowledgeContent content,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A knowledge document must belong to a workspace.")
            {
                Code = "knowledge.tenant.required",
            };
        }

        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);

        return new KnowledgeDocument(
            KnowledgeDocumentId.New(),
            tenantId,
            title,
            content,
            RequireInstant(createdAt, "created"));
    }

    /// <summary>
    /// Rebuilds an aggregate from storage.
    /// </summary>
    /// <remarks>
    /// Present because the persistence layer must be able to restore a document
    /// exactly as it was, including an index state that <see cref="Create"/> can
    /// never produce. It performs no lifecycle validation for that reason: it is
    /// not a second way to make a document, and nothing outside persistence calls
    /// it.
    /// </remarks>
    public static KnowledgeDocument Restore(
        KnowledgeDocumentId id,
        TenantId tenantId,
        KnowledgeTitle title,
        KnowledgeContent content,
        bool isEnabled,
        ContentFingerprint? indexedFingerprint,
        EmbeddingProfileId? indexedProfile,
        DateTimeOffset? indexedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);

        return new KnowledgeDocument(id, tenantId, title, content, createdAt)
        {
            IsEnabled = isEnabled,
            IndexedFingerprint = indexedFingerprint,
            IndexedProfile = indexedProfile,
            IndexedAt = indexedAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Replaces the title and the text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Changing the content invalidates the index, and changing only the
    /// title does not.</strong> The vectors describe the text; renaming a document
    /// does not make them wrong, and forcing somebody to pay for a re-index
    /// because they fixed a typo in a heading would be an invented cost. Editing
    /// the text does make them wrong, and the readiness derivation catches that on
    /// its own -- the fingerprint moves and the stored one no longer matches.
    /// </para>
    /// <para>
    /// Nothing is cleared when the content changes. The old fingerprint, profile
    /// and instant stay exactly as they were, and the chunks behind them stay in
    /// the database. That is deliberate: they are the honest record of what was
    /// indexed and when, they are what an operator needs in order to understand
    /// what happened, and they are unreachable anyway because retrieval requires
    /// the indexed fingerprint to equal the current one.
    /// </para>
    /// </remarks>
    public void Revise(KnowledgeTitle title, KnowledgeContent content, DateTimeOffset revisedAt)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);

        Title = title;
        Content = content;

        RecordUpdate(RequireInstant(revisedAt, "revised"));
    }

    /// <summary>Records that this exact text has been indexed under a profile.</summary>
    /// <remarks>
    /// It takes no fingerprint from the caller and derives one from the content it
    /// is holding, so an indexer cannot report success for text this document does
    /// not have. Making sure the document has not moved underneath it while the
    /// embeddings were being produced is the version check, and that happens in
    /// the same transaction as this.
    /// </remarks>
    public void MarkIndexed(EmbeddingProfileId profile, DateTimeOffset indexedAt)
    {
        ArgumentNullException.ThrowIfNull(profile);

        DateTimeOffset instant = RequireInstant(indexedAt, "indexed");

        IndexedFingerprint = Fingerprint;
        IndexedProfile = profile;
        IndexedAt = instant;

        RecordUpdate(instant);
    }

    /// <summary>Puts the document back into use.</summary>
    public void Enable(DateTimeOffset enabledAt)
    {
        if (IsEnabled)
        {
            throw new DomainStateException("This knowledge document is already enabled.")
            {
                Code = "knowledge.document.already_enabled",
            };
        }

        IsEnabled = true;

        RecordUpdate(RequireInstant(enabledAt, "enabled"));
    }

    /// <summary>Takes the document out of use without discarding it.</summary>
    public void Disable(DateTimeOffset disabledAt)
    {
        if (!IsEnabled)
        {
            throw new DomainStateException("This knowledge document is already disabled.")
            {
                Code = "knowledge.document.already_disabled",
            };
        }

        IsEnabled = false;

        RecordUpdate(RequireInstant(disabledAt, "disabled"));
    }

    private void RecordUpdate(DateTimeOffset instant)
    {
        if (instant < UpdatedAt)
        {
            throw new DomainValidationException(
                "A knowledge document cannot be updated at an instant before its last update.")
            {
                Code = "knowledge.instant.invalid",
            };
        }

        UpdatedAt = instant;
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant, string what)
    {
        if (instant == default)
        {
            throw new DomainValidationException(
                $"The instant a knowledge document was {what} is required.")
            {
                Code = "knowledge.instant.invalid",
            };
        }

        return instant.ToUniversalTime();
    }
}
