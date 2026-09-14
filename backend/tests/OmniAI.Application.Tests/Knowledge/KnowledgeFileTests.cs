using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Users;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Uploading a file into Knowledge, and replacing the file behind a document.
/// </summary>
/// <remarks>
/// <strong>The claim these exist to keep honest is that uploading spends
/// nothing.</strong> Every test here asserts the embedding provider was never
/// reached and no chunk moved — because a platform where dropping a folder of
/// documents onto a page quietly bought a few hundred embeddings would be one
/// nobody could safely use.
/// </remarks>
public sealed class KnowledgeFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    // ---- uploading -------------------------------------------------------

    [Fact]
    public async Task An_uploaded_file_becomes_a_document_that_needs_an_index()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("hours.txt", "Nine to five.");

        Assert.Equal("Nine to five.", created.Value.Document.Content);
        Assert.Equal(KnowledgeReadiness.NeedsIndex, created.Value.Document.Readiness);

        // The whole point: extraction is free, indexing is a decision.
        Assert.Equal(0, world.Embeddings.Calls);
        Assert.Equal(0, world.Index.Replacements);
    }

    [Fact]
    public async Task The_source_records_which_file_the_text_came_from()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("plans.csv", "Plan\nBasic");

        KnowledgeSourceView source = created.Value.Source;

        Assert.Equal("plans.csv", source.FileName);
        Assert.Equal(KnowledgeFileType.Csv, source.FileType);
        Assert.Equal(Now, source.ExtractedAt);
        Assert.True(source.ByteLength > 0);
    }

    [Fact]
    public async Task A_title_is_derived_from_the_file_name_when_none_is_given()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created =
            await world.UploadAsync("Refund policy.pdf", "Refunds within 30 days.");

        Assert.Equal("Refund policy", created.Value.Document.Title);
    }

    [Fact]
    public async Task An_explicit_title_wins()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created =
            await world.UploadAsync("export-final-v3.csv", "Plan\nBasic", title: "Pricing");

        Assert.Equal("Pricing", created.Value.Document.Title);
    }

    [Fact]
    public async Task A_path_in_the_file_name_never_reaches_the_stored_name()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created =
            await world.UploadAsync("../../etc/passwd.txt", "root:x:0:0");

        Assert.Equal("passwd.txt", created.Value.Source.FileName);
        Assert.DoesNotContain("..", created.Value.Source.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_the_platform_will_not_read_creates_nothing()
    {
        World world = World.Build();

        world.Extractor.Refuse(KnowledgeFileRejection.NoText, "No extractable text was found.");

        await Assert.ThrowsAsync<KnowledgeFileRejectedException>(
            () => world.UploadAsync("scan.pdf", "irrelevant"));

        Assert.Empty(world.Documents.Stored);
        Assert.Equal(0, world.Embeddings.Calls);
    }

    [Fact]
    public async Task Somebody_without_the_permission_cannot_upload()
    {
        World world = World.Build(canManage: false);

        await Assert.ThrowsAnyAsync<Exception>(() => world.UploadAsync("hours.txt", "Nine to five."));

        Assert.Empty(world.Documents.Stored);
    }

    // ---- replacing -------------------------------------------------------

    [Fact]
    public async Task Replacing_the_file_replaces_the_text_and_invalidates_the_index()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("hours.txt", "Nine to five.");

        // Indexed, so there is a Ready state for the replacement to invalidate.
        world.MarkIndexed(created.Value.Document.Id);

        Versioned<KnowledgeFileResult>? replaced = await world.ReplaceAsync(
            created.Value.Document.Id,
            "hours.txt",
            "Ten to six.",
            world.VersionOf(created.Value.Document.Id));

        Assert.NotNull(replaced);
        Assert.Equal("Ten to six.", replaced.Value.Document.Content);

        // New text means the stored chunks describe something nobody can see, and
        // the honest state for that is not Ready.
        Assert.Equal(KnowledgeReadiness.NeedsIndex, replaced.Value.Document.Readiness);
        Assert.Equal(0, world.Embeddings.Calls);
        Assert.Equal(0, world.Index.Replacements);
    }

    [Fact]
    public async Task Replacing_advances_the_version()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("a.txt", "First.");

        ConcurrencyToken before = world.VersionOf(created.Value.Document.Id);

        Versioned<KnowledgeFileResult>? replaced = await world.ReplaceAsync(
            created.Value.Document.Id,
            "a.txt",
            "Second.",
            before);

        Assert.NotEqual(before, replaced!.Version);
    }

    [Fact]
    public async Task Replacing_against_a_stale_version_is_refused_and_changes_nothing()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("a.txt", "First.");

        ConcurrencyToken stale = world.VersionOf(created.Value.Document.Id);

        await world.ReplaceAsync(created.Value.Document.Id, "a.txt", "Second.", stale);

        await Assert.ThrowsAsync<KnowledgeDocumentVersionMismatchException>(
            () => world.ReplaceAsync(created.Value.Document.Id, "a.txt", "Third.", stale));

        Assert.Equal("Second.", world.ContentOf(created.Value.Document.Id));
    }

    [Fact]
    public async Task Re_uploading_the_identical_file_writes_nothing()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("plans.csv", "Plan\nBasic");

        world.MarkIndexed(created.Value.Document.Id);

        ConcurrencyToken before = world.VersionOf(created.Value.Document.Id);

        Versioned<KnowledgeFileResult>? replaced = await world.ReplaceAsync(
            created.Value.Document.Id,
            "plans.csv",
            "Plan\nBasic",
            before);

        // <strong>Same file, same text, nothing happens.</strong> The version does
        // not move, so every open screen's entity tag stays valid; and because the
        // text is unchanged, a document that was Ready stays Ready rather than
        // being told to re-index something identical.
        Assert.Equal(before, replaced!.Version);
        Assert.Equal(before, world.VersionOf(created.Value.Document.Id));
        Assert.Equal(KnowledgeReadiness.Ready, replaced.Value.Document.Readiness);
        Assert.Equal(0, world.Embeddings.Calls);
    }

    [Fact]
    public async Task An_unreadable_replacement_leaves_the_existing_text_alone()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult> created = await world.UploadAsync("a.txt", "Original.");

        world.MarkIndexed(created.Value.Document.Id);

        world.Extractor.Refuse(KnowledgeFileRejection.Unreadable, "That file could not be read.");

        await Assert.ThrowsAsync<KnowledgeFileRejectedException>(
            () => world.ReplaceAsync(
                created.Value.Document.Id,
                "a.txt",
                "never read",
                world.VersionOf(created.Value.Document.Id)));

        // The document is exactly as it was: same text, same readiness, same
        // chunks. A failed upload must not cost somebody the knowledge they had.
        Assert.Equal("Original.", world.ContentOf(created.Value.Document.Id));
        Assert.Equal(KnowledgeReadiness.Ready, world.ReadinessOf(created.Value.Document.Id));
        Assert.Equal(0, world.Index.Replacements);
    }

    [Fact]
    public async Task A_document_written_by_hand_has_no_file_to_replace()
    {
        World world = World.Build();

        KnowledgeDocument typed = KnowledgeDocument.Create(
            world.Workspace,
            KnowledgeTitle.Create("Typed"),
            KnowledgeContent.Create("Written by a person."),
            Now);

        world.Documents.Seed(typed);

        await Assert.ThrowsAsync<KnowledgeDocumentHasNoSourceException>(
            () => world.ReplaceAsync(
                typed.Id.Value,
                "a.txt",
                "uploaded",
                world.Documents.VersionOf(typed)));

        Assert.Equal("Written by a person.", world.ContentOf(typed.Id.Value));
    }

    [Fact]
    public async Task A_document_from_another_workspace_is_simply_not_found()
    {
        World world = World.Build();

        Versioned<KnowledgeFileResult>? replaced = await world.ReplaceAsync(
            Guid.CreateVersion7(),
            "a.txt",
            "text",
            ConcurrencyToken.From("1"));

        Assert.Null(replaced);
    }

    /// <summary>One workspace, its documents, and the two handlers.</summary>
    private sealed class World
    {
        public required TenantId Workspace { get; init; }

        public required InMemoryKnowledgeDocumentRepository Documents { get; init; }

        public required InMemoryKnowledgeSourceRepository Sources { get; init; }

        public required StubKnowledgeFileExtractor Extractor { get; init; }

        public required RecordingEmbeddingGenerator Embeddings { get; init; }

        public required RecordingKnowledgeIndexStore Index { get; init; }

        public required UploadKnowledgeFileHandler Upload { get; init; }

        public required ReplaceKnowledgeSourceHandler Replace { get; init; }

        public static World Build(bool canManage = true)
        {
            TenantId workspace = TenantId.New();

            InMemoryKnowledgeDocumentRepository documents = new();
            InMemoryKnowledgeSourceRepository sources = new(documents);
            StubKnowledgeFileExtractor extractor = new();
            RecordingEmbeddingGenerator embeddings = new();
            RecordingKnowledgeIndexStore index = new(documents);
            FixedTimeProvider clock = new(Now);

            // A real authorizer over real role aggregates, never a permissive
            // stub: a stub would let every one of these pass while proving
            // nothing about whether the handler consults authorisation at all.
            TenantAuthorizer authority = canManage
                ? TestAuthority.Owner(UserId.New(), workspace.Value)
                : TestAuthority.With(
                    UserId.New(),
                    workspace.Value,
                    PlatformPermissions.KnowledgeView);

            return new World
            {
                Workspace = workspace,
                Documents = documents,
                Sources = sources,
                Extractor = extractor,
                Embeddings = embeddings,
                Index = index,
                Upload = new UploadKnowledgeFileHandler(
                    sources,
                    extractor,
                    authority,
                    embeddings,
                    clock),
                Replace = new ReplaceKnowledgeSourceHandler(
                    documents,
                    sources,
                    extractor,
                    authority,
                    embeddings,
                    clock),
            };
        }

        public Task<Versioned<KnowledgeFileResult>> UploadAsync(
            string fileName,
            string text,
            string? title = null)
        {
            Extractor.Returns(text, TypeFor(fileName));

            return Upload.HandleAsync(new UploadKnowledgeFileCommand(
                Workspace.Value,
                title,
                new KnowledgeFileUpload(fileName, null, System.Text.Encoding.UTF8.GetBytes(text))));
        }

        public Task<Versioned<KnowledgeFileResult>?> ReplaceAsync(
            Guid documentId,
            string fileName,
            string text,
            ConcurrencyToken expected)
        {
            Extractor.Returns(text, TypeFor(fileName));

            return Replace.HandleAsync(new ReplaceKnowledgeSourceCommand(
                Workspace.Value,
                documentId,
                new KnowledgeFileUpload(fileName, null, System.Text.Encoding.UTF8.GetBytes(text)),
                expected));
        }

        public ConcurrencyToken VersionOf(Guid documentId) =>
            Documents.VersionOf(Find(documentId));

        public string ContentOf(Guid documentId) => Find(documentId).Content.Value;

        public KnowledgeReadiness ReadinessOf(Guid documentId) =>
            Find(documentId).ReadinessFor(Embeddings.Profile.Id);

        /// <summary>Marks a document indexed, so a replacement has something to invalidate.</summary>
        public void MarkIndexed(Guid documentId) =>
            Find(documentId).MarkIndexed(Embeddings.Profile.Id, Now);

        private KnowledgeDocument Find(Guid documentId) =>
            Documents.Stored.First(document => document.Id.Value == documentId);

        private static KnowledgeFileType TypeFor(string fileName) =>
            System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".pdf" => KnowledgeFileType.Pdf,
                ".docx" => KnowledgeFileType.Docx,
                ".csv" => KnowledgeFileType.Csv,
                _ => KnowledgeFileType.Text,
            };
    }
}

/// <summary>
/// An extractor a test drives.
/// </summary>
/// <remarks>
/// The real parsers are exercised against real files in the Infrastructure
/// suite. What these tests need is the two answers a handler has to deal with:
/// text, and a refusal.
/// </remarks>
internal sealed class StubKnowledgeFileExtractor : IKnowledgeFileExtractor
{
    private string _text = "text";
    private KnowledgeFileType _type = KnowledgeFileType.Text;
    private KnowledgeFileRejectedException? _refusal;

    /// <summary>How many files were handed to it.</summary>
    public int Calls { get; private set; }

    /// <summary>What the next extraction produces, unless a refusal is armed.</summary>
    /// <remarks>
    /// An armed refusal wins, because the test that armed it means the file to be
    /// rejected however the scenario helper happens to describe it.
    /// </remarks>
    public void Returns(string text, KnowledgeFileType type)
    {
        _text = text;
        _type = type;
    }

    public void Refuse(KnowledgeFileRejection reason, string message) =>
        _refusal = new KnowledgeFileRejectedException(reason, message);

    public KnowledgeExtraction Extract(KnowledgeFileUpload upload)
    {
        Calls++;

        return _refusal is not null
            ? throw _refusal
            : new KnowledgeExtraction(_text, _type);
    }
}

/// <summary>Stored sources, in memory, beside the document repository.</summary>
internal sealed class InMemoryKnowledgeSourceRepository(
    InMemoryKnowledgeDocumentRepository documents)
    : IKnowledgeSourceRepository
{
    private readonly InMemoryKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly List<KnowledgeSource> _sources = [];

    public IReadOnlyList<KnowledgeSource> Stored => _sources;

    public Task<KnowledgeSource?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_sources.Find(source =>
            source.KnowledgeDocumentId == documentId && source.TenantId == tenantId));

    public Task<IReadOnlyDictionary<Guid, KnowledgeSource>> ListForAsync(
        TenantId tenantId,
        IReadOnlyCollection<KnowledgeDocumentId> documentIds,
        CancellationToken cancellationToken = default)
    {
        HashSet<KnowledgeDocumentId> wanted = [.. documentIds];

        return Task.FromResult<IReadOnlyDictionary<Guid, KnowledgeSource>>(
            _sources
                .Where(source =>
                    source.TenantId == tenantId && wanted.Contains(source.KnowledgeDocumentId))
                .ToDictionary(source => source.KnowledgeDocumentId.Value));
    }

    public Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        _sources.Add(source);

        return _documents.AddAsync(document, cancellationToken);
    }

    public Task<ConcurrencyToken> ReplaceAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default) =>
        _documents.UpdateAsync(document, expected, cancellationToken);
}
