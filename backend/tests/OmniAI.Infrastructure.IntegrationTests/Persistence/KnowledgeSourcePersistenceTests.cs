using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What PostgreSQL itself enforces about knowledge sources.
/// </summary>
/// <remarks>
/// Every property here is also checked in application code; asserted against the
/// real database, they keep holding when somebody refactors the code that checks
/// them.
/// </remarks>
public sealed class KnowledgeSourcePersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_source_and_its_document_are_written_together()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(tenant, "policy.pdf");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        await using OmniAiDbContext read = database.CreateContext();

        KnowledgeSource? stored = await new KnowledgeSourceRepository(read)
            .FindAsync(tenant.Id, document.Id);

        Assert.NotNull(stored);
        Assert.Equal("policy.pdf", stored.FileName.Value);
        Assert.Equal(KnowledgeFileType.Pdf, stored.FileType);
        Assert.Equal(source.Fingerprint, stored.Fingerprint);
        Assert.Equal(source.ByteLength, stored.ByteLength);
    }

    [Fact]
    public async Task A_source_from_another_workspace_is_simply_not_found()
    {
        Tenant owner = await NewTenantAsync();
        Tenant outsider = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(owner, "policy.pdf");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        await using OmniAiDbContext read = database.CreateContext();

        Assert.Null(await new KnowledgeSourceRepository(read).FindAsync(outsider.Id, document.Id));
    }

    [Fact]
    public async Task A_source_cannot_point_at_another_workspaces_document()
    {
        // The composite foreign key. Without it a row could name a document
        // belonging to somebody else, and only application code would stand
        // between that and one company's file metadata on another's knowledge.
        Tenant owner = await NewTenantAsync();
        Tenant outsider = await NewTenantAsync();

        (KnowledgeDocument document, _) = Pair(owner, "policy.pdf");

        await using (OmniAiDbContext seed = database.CreateContext())
        {
            await new KnowledgeDocumentRepository(seed, new StubEmbeddingGenerator())
                .AddAsync(document);
        }

        KnowledgeSource crossTenant = KnowledgeSource.Create(
            outsider.Id,
            document.Id,
            SourceFileName.Create("stolen.pdf"),
            KnowledgeFileType.Pdf,
            100,
            Fingerprint(9),
            Now);

        await using OmniAiDbContext context = database.CreateContext();

        context.KnowledgeSources.Add(crossTenant);

        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "fk_knowledge_sources_document",
            (failure.InnerException as PostgresException)?.ConstraintName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_document_can_have_only_one_source()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(tenant, "first.pdf");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        // "Which file is this document from" has one answer. A second row would
        // make it two, and nothing later could say which was current.
        KnowledgeSource second = KnowledgeSource.Create(
            tenant.Id,
            document.Id,
            SourceFileName.Create("second.pdf"),
            KnowledgeFileType.Pdf,
            200,
            Fingerprint(2),
            Now);

        await using OmniAiDbContext duplicate = database.CreateContext();

        duplicate.KnowledgeSources.Add(second);

        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());

        Assert.Contains(
            "ux_knowledge_sources_document",
            (failure.InnerException as PostgresException)?.ConstraintName ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replacing_a_source_is_refused_when_the_document_moved_first()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(tenant, "v1.csv");

        ConcurrencyToken original;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            original = await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        // Somebody else edits the document, exactly as a second administrator
        // in a second tab would.
        await using (OmniAiDbContext other = database.CreateContext())
        {
            KnowledgeDocument tracked = await other.KnowledgeDocuments
                .SingleAsync(candidate => candidate.Id == document.Id);

            tracked.Revise(tracked.Title, KnowledgeContent.Create("Changed."), Now.AddMinutes(1));

            await other.SaveChangesAsync();
        }

        await using OmniAiDbContext stale = database.CreateContext();

        KnowledgeSourceRepository repository = new(stale);

        KnowledgeDocument loaded = await stale.KnowledgeDocuments
            .SingleAsync(candidate => candidate.Id == document.Id);

        KnowledgeSource storedSource = (await repository.FindAsync(tenant.Id, document.Id))!;

        storedSource.Replace(
            SourceFileName.Create("v2.csv"),
            KnowledgeFileType.Csv,
            200,
            Fingerprint(2),
            Now.AddMinutes(2));

        loaded.Revise(loaded.Title, KnowledgeContent.Create("Replaced."), Now.AddMinutes(2));

        await Assert.ThrowsAsync<KnowledgeDocumentVersionMismatchException>(
            () => repository.ReplaceAsync(loaded, storedSource, original));
    }

    [Fact]
    public async Task A_replacement_writes_the_document_and_the_source_together()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(tenant, "v1.csv");

        ConcurrencyToken version;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            version = await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        await using (OmniAiDbContext context = database.CreateContext())
        {
            KnowledgeSourceRepository repository = new(context);

            KnowledgeDocument loaded = await context.KnowledgeDocuments
                .SingleAsync(candidate => candidate.Id == document.Id);

            KnowledgeSource storedSource = (await repository.FindAsync(tenant.Id, document.Id))!;

            storedSource.Replace(
                SourceFileName.Create("v2.csv"),
                KnowledgeFileType.Csv,
                200,
                Fingerprint(2),
                Now.AddMinutes(2));

            loaded.Revise(loaded.Title, KnowledgeContent.Create("Replaced."), Now.AddMinutes(2));

            await repository.ReplaceAsync(loaded, storedSource, version);
        }

        await using OmniAiDbContext read = database.CreateContext();

        KnowledgeSource? after = await new KnowledgeSourceRepository(read)
            .FindAsync(tenant.Id, document.Id);

        KnowledgeDocument reloaded = await read.KnowledgeDocuments
            .SingleAsync(candidate => candidate.Id == document.Id);

        Assert.Equal("v2.csv", after!.FileName.Value);
        Assert.Equal(Fingerprint(2), after.Fingerprint);
        Assert.Equal("Replaced.", reloaded.Content.Value);
    }

    [Fact]
    public async Task A_list_says_which_documents_came_from_a_file()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument uploaded, KnowledgeSource source) = Pair(tenant, "plans.csv");

        KnowledgeDocument typed = KnowledgeDocument.Create(
            tenant.Id,
            KnowledgeTitle.Create("Written by hand"),
            KnowledgeContent.Create("Somebody typed this."),
            Now);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeSourceRepository(context).AddAsync(uploaded, source);
        }

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeDocumentRepository(context, new StubEmbeddingGenerator())
                .AddAsync(typed);
        }

        await using OmniAiDbContext read = database.CreateContext();

        KnowledgeDocumentRepository documents = new(read, new StubEmbeddingGenerator());

        Application.Knowledge.ListKnowledgeDocuments.KnowledgeDocumentPage page =
            await documents.ListAsync(tenant.Id, 1, 50);

        // A manual document is first-class and stays that way: it simply has no
        // file, which is what the absent type says.
        Assert.Equal(
            KnowledgeFileType.Csv,
            page.Items.Single(item => item.Id == uploaded.Id.Value).SourceFileType);

        Assert.Null(page.Items.Single(item => item.Id == typed.Id.Value).SourceFileType);
    }

    [Fact]
    public async Task The_stored_row_carries_no_file_content()
    {
        Tenant tenant = await NewTenantAsync();

        (KnowledgeDocument document, KnowledgeSource source) = Pair(tenant, "policy.pdf");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new KnowledgeSourceRepository(context).AddAsync(document, source);
        }

        // The storage decision, asserted against the schema. Anybody looking for
        // the original file will not find one, which is the intended answer.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name, ', ' ORDER BY ordinal_position) "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'knowledge_sources'");

        Assert.Equal(
            "id, tenant_id, knowledge_document_id, file_name, file_type, byte_length, "
            + "fingerprint, extracted_at, created_at, updated_at",
            columns as string);
    }

    private static (KnowledgeDocument Document, KnowledgeSource Source) Pair(
        Tenant tenant,
        string fileName)
    {
        KnowledgeDocument document = KnowledgeDocument.Create(
            tenant.Id,
            KnowledgeTitle.Create("Uploaded"),
            KnowledgeContent.Create("Text read out of a file."),
            Now);

        KnowledgeSource source = KnowledgeSource.Create(
            tenant.Id,
            document.Id,
            SourceFileName.Create(fileName),
            TypeFor(fileName),
            1234,
            Fingerprint(1),
            Now);

        return (document, source);
    }

    private static KnowledgeFileType TypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => KnowledgeFileType.Pdf,
            ".docx" => KnowledgeFileType.Docx,
            ".csv" => KnowledgeFileType.Csv,
            _ => KnowledgeFileType.Text,
        };

    private static ContentFingerprint Fingerprint(byte seed)
    {
        byte[] digest = new byte[ContentFingerprint.Length];

        Array.Fill(digest, seed);

        return ContentFingerprint.From(digest);
    }

    private async Task<Tenant> NewTenantAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant;
    }
}

/// <summary>
/// A provider that is never called.
/// </summary>
/// <remarks>
/// The document repository needs a profile to work out readiness against. These
/// tests are about the schema and the source rows, so the profile is fixed and
/// generating an embedding would be a failure rather than a fallback.
/// </remarks>
internal sealed class StubEmbeddingGenerator : Application.Knowledge.ITextEmbeddingGenerator
{
    public Application.Knowledge.EmbeddingProfile Profile { get; } =
        Application.Knowledge.EmbeddingProfile.Create(
            EmbeddingProfileId.Create("test:fake:8"),
            dimensions: 8,
            maxBatchSize: 4,
            maxInputCharacters: 8_000);

    public Task<IReadOnlyList<Application.Knowledge.EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "These tests are about stored rows. Nothing here should reach a provider.");
}
