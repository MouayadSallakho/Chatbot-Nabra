using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Reports;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Reports;
using OmniAI.Infrastructure.Reports.Rendering;
using OmniAI.Testing;
using UglyToad.PdfPig;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// The generator end to end: real data, real stores, real files on a real disk.
/// </summary>
/// <remarks>
/// Every report type in every format is generated over a workspace seeded with
/// leak canaries, and the finished files are opened and searched. A second
/// workspace is seeded beside it so the same files prove tenant isolation.
/// </remarks>
public sealed class ReportGenerationTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>, IDisposable
{
    private const string RivalAgent = "Rival Agent Zeta";
    private const string RivalCustomer = "Rival Customer Omega";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omniai-report-gen-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public static TheoryData<ReportType, ReportFormat> Everything()
    {
        TheoryData<ReportType, ReportFormat> data = [];

        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            foreach (ReportFormat format in Enum.GetValues<ReportFormat>())
            {
                data.Add(type, format);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public async Task Every_report_is_a_complete_valid_file_that_carries_no_secret_and_no_other_workspace(
        ReportType type,
        ReportFormat format)
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportWorld rival = await ReportWorld.CreateAsync(database, "Rival Trading");
        await rival.SeedActivityAsync(RivalCustomer, RivalAgent);

        long usageBefore = await UsageRowsAsync();

        ReportExecution execution = await world.PendingAsync(type, format);
        ReportGenerationResult result = await GenerateAsync(execution);

        Assert.True(result.Outcome == ReportGenerationOutcome.Completed, result.Fault?.ToString());

        // Nothing called a provider: not one accounted operation was added.
        Assert.Equal(usageBefore, await UsageRowsAsync());

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionStore store = new(context);

        ReportExecutionRecord record = (await store.FindAsync(world.Tenant, execution.Id))!;
        ReportArtifact artifact = (await store.FindArtifactAsync(world.Tenant, execution.Id))!;

        Assert.Equal(ReportExecutionStatus.Completed, record.Execution.Status);
        Assert.Equal(ReportCatalog.ContentType(format), artifact.ContentType);

        byte[] bytes = await ReadAsync(artifact.StorageKey);

        Assert.Equal(artifact.ByteLength, bytes.LongLength);
        Assert.Equal(artifact.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));

        string visible = Visible(format, bytes);

        foreach (string canary in new[]
        {
            ReportWorld.MessageCanary,
            ReportWorld.InstructionsCanary,
            ReportWorld.KnowledgeCanary,
            ReportWorld.RawNumberCanary,
            ReportWorld.PublicKeyCanary,
            "@example.test",
            RivalAgent,
            RivalCustomer,
            "OPENAI_API_KEY",
            artifact.StorageKey,
        })
        {
            Assert.DoesNotContain(canary, visible, StringComparison.Ordinal);
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "incoming")));
    }

    [Fact]
    public async Task Customer_activity_names_customers_safely_and_counts_their_activity()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportExecution execution = await world.PendingAsync(ReportType.CustomerActivity, ReportFormat.Csv);
        await GenerateAsync(execution);

        await using OmniAiDbContext context = database.CreateContext();
        ReportArtifact artifact = (await new ReportExecutionStore(context).FindArtifactAsync(world.Tenant, execution.Id))!;

        List<string[]> rows = ReportRenderingTests.ReadCsv(await ReadAsync(artifact.StorageKey));

        Assert.Equal(3, rows.Count);

        // Most recent first: the WhatsApp customer wrote a day later.
        Assert.Equal("WhatsApp customer", rows[1][1]);
        Assert.Equal("WhatsApp", rows[1][2]);

        // The hostile display name is there, and cannot run.
        Assert.Equal("'=HYPERLINK(\"https://evil.example\")", rows[2][1]);
        Assert.Equal("Website", rows[2][2]);
        Assert.Equal("1", rows[2][3]);
        Assert.Equal("1", rows[2][5]);
    }

    [Fact]
    public async Task Provider_cost_never_adds_two_currencies()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportExecution execution = await world.PendingAsync(ReportType.ProviderCost, ReportFormat.Xlsx);
        await GenerateAsync(execution);

        await using OmniAiDbContext context = database.CreateContext();
        ReportArtifact artifact = (await new ReportExecutionStore(context).FindArtifactAsync(world.Tenant, execution.Id))!;

        string text = Visible(ReportFormat.Xlsx, await ReadAsync(artifact.StorageKey));

        Assert.Contains("USD", text, StringComparison.Ordinal);
        Assert.Contains("EUR", text, StringComparison.Ordinal);
        Assert.Contains("Estimated provider cost", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual cost", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Amount due", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoice", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_requester_who_lost_the_data_permission_gets_a_failure_and_no_file()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        UserId member = await world.MemberAsync(
            PlatformPermissions.ReportsView, PlatformPermissions.ReportsGenerate, PlatformPermissions.CustomersView);

        ReportExecution execution = await world.PendingAsync(ReportType.CustomerActivity, ReportFormat.Csv, member);

        await world.RevokeAsync(member);

        ReportGenerationResult result = await GenerateAsync(execution);

        Assert.Equal(ReportGenerationOutcome.Failed, result.Outcome);

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionRecord record = (await new ReportExecutionStore(context).FindAsync(world.Tenant, execution.Id))!;

        Assert.Equal(ReportFailures.PermissionRevoked, record.Execution.FailureCode);
        Assert.Null(await new ReportExecutionStore(context).FindArtifactAsync(world.Tenant, execution.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "artifacts")));
    }

    [Fact]
    public async Task A_report_over_the_export_bound_fails_clearly_and_leaves_no_file()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportExecution execution = await world.PendingAsync(ReportType.CustomerActivity, ReportFormat.Xlsx);

        ReportGenerationResult result = await GenerateAsync(execution, new ReportOptions { MaxRows = 1, MaxPdfRows = 1 });

        Assert.Equal(ReportGenerationOutcome.Failed, result.Outcome);

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionRecord record = (await new ReportExecutionStore(context).FindAsync(world.Tenant, execution.Id))!;

        Assert.Equal(ReportFailures.TooLarge, record.Execution.FailureCode);
        Assert.Contains("exceeds the supported export size", ReportFailures.Describe(record.Execution.FailureCode), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task An_artifact_over_the_byte_bound_fails_and_leaves_no_file()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportExecution execution = await world.PendingAsync(ReportType.ConversationActivity, ReportFormat.Pdf);

        ReportGenerationResult result = await GenerateAsync(execution, new ReportOptions { MaxArtifactBytes = 1024 });

        Assert.Equal(ReportGenerationOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Retention_removes_the_bytes_and_the_download_truthfully_ends()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();

        ReportExecution execution = await world.PendingAsync(ReportType.ChannelUsage, ReportFormat.Csv);
        await GenerateAsync(execution);

        FileSystemReportArtifactStore files = new(_root);
        string key;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            key = (await new ReportExecutionStore(context).FindArtifactAsync(world.Tenant, execution.Id))!.StorageKey;

            // Not yet: a sweep now removes nothing of this report's.
            await new ReportRetention(new ReportExecutionStore(context), files, TimeProvider.System).SweepAsync(500);
            Assert.NotNull(await files.OpenReadAsync(key));
        }

        await using (OmniAiDbContext later = database.CreateContext())
        {
            await new ReportRetention(
                    new ReportExecutionStore(later),
                    files,
                    new FixedTimeProvider(DateTimeOffset.UtcNow.AddDays(31)))
                .SweepAsync(500);
        }

        await using OmniAiDbContext check = database.CreateContext();
        ReportExecutionRecord record = (await new ReportExecutionStore(check).FindAsync(world.Tenant, execution.Id))!;

        Assert.Equal(ReportExecutionStatus.Expired, record.Execution.Status);
        Assert.NotNull(record.Execution.ExpiredAt);
        Assert.Null(await files.OpenReadAsync(key));
        Assert.False(File.Exists(Path.Combine(_root, "artifacts", key)));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ReportGenerationResult> GenerateAsync(ReportExecution execution, ReportOptions? options = null)
    {
        await using OmniAiDbContext claiming = database.CreateContext();

        ClaimedReportExecution claimed = (await new ReportExecutionStore(claiming)
                .ClaimAsync(500, 3, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow))
            .Single(candidate => candidate.Id == execution.Id);

        await using OmniAiDbContext context = database.CreateContext();

        ReportGenerator generator = new(
            new ReportExecutionStore(context),
            new FileSystemReportArtifactStore(_root),
            new ReportAuthority(new TenantMembershipRepository(context), new TenantRoleRepository(context)),
            new ReportDocumentBuilder(
                new global::OmniAI.Infrastructure.Analytics.WorkspaceAnalyticsReader(context),
                new CustomerActivityReader(context)),
            [new CsvReportRenderer(), new XlsxReportRenderer(), new PdfReportRenderer()],
            new TenantRepository(context),
            options ?? new ReportOptions(),
            TimeProvider.System);

        return await generator.GenerateAsync(claimed, 3);
    }

    private async Task<byte[]> ReadAsync(string key)
    {
        await using Stream stream = (await new FileSystemReportArtifactStore(_root).OpenReadAsync(key))!;
        using MemoryStream copy = new();

        await stream.CopyToAsync(copy);

        return copy.ToArray();
    }

    private async Task<long> UsageRowsAsync()
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.AiProviderUsages.LongCountAsync();
    }

    /// <summary>Everything a reader of the file could see, as text.</summary>
    private static string Visible(ReportFormat format, byte[] bytes)
    {
        switch (format)
        {
            case ReportFormat.Csv:
                return Encoding.UTF8.GetString(bytes);

            case ReportFormat.Xlsx:
            {
                using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);
                StringBuilder all = new();

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    using StreamReader reader = new(entry.Open());
                    all.Append(reader.ReadToEnd());
                }

                return all.ToString();
            }

            default:
            {
                using PdfDocument pdf = PdfDocument.Open(bytes);

                return string.Concat(pdf.GetPages().Select(page => page.Text)) + Encoding.Latin1.GetString(bytes);
            }
        }
    }
}
