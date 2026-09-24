using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.Reports;
using OmniAI.Infrastructure.Reports;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// The filesystem artifact store, against a real temporary directory.
/// </summary>
public sealed class FileSystemReportArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omniai-report-store-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_write_is_hashed_measured_and_readable_only_once_finished()
    {
        FileSystemReportArtifactStore store = new(_root);
        byte[] content = Encoding.UTF8.GetBytes("a,b\r\n1,2\r\n");

        StoredReportFile stored = await store.WriteAsync(
            async (stream, token) =>
            {
                // Mid-write, nothing is readable under any key and nothing is in
                // the final directory.
                Assert.Empty(Directory.GetFiles(Path.Combine(_root, "artifacts")));
                await stream.WriteAsync(content, token);
            },
            1024);

        Assert.Matches("^[0-9a-f]{32}$", stored.StorageKey);
        Assert.Equal(content.Length, stored.ByteLength);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), stored.Sha256);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "incoming")));

        await using Stream? read = await store.OpenReadAsync(stored.StorageKey);
        Assert.NotNull(read);

        using MemoryStream copy = new();
        await read.CopyToAsync(copy);
        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task A_write_over_the_limit_leaves_nothing_behind()
    {
        FileSystemReportArtifactStore store = new(_root);

        await Assert.ThrowsAsync<ReportTooLargeException>(() => store.WriteAsync(
            (stream, token) => stream.WriteAsync(new byte[2048], token).AsTask(),
            1024));

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "artifacts")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "incoming")));
    }

    [Fact]
    public async Task A_failed_render_leaves_nothing_behind()
    {
        FileSystemReportArtifactStore store = new(_root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(
            async (stream, token) =>
            {
                await stream.WriteAsync(new byte[100], token);
                throw new InvalidOperationException("renderer broke half way");
            },
            1024));

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\reports\\x")]
    [InlineData("0123456789abcdef0123456789abcdef/../../x")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("")]
    public async Task A_key_that_is_not_a_server_key_names_nothing(string key)
    {
        FileSystemReportArtifactStore store = new(_root);

        Assert.Null(await store.OpenReadAsync(key));

        // Deleting through a forged key is a no-op, never a delete elsewhere.
        await store.DeleteAsync(key);
    }

    [Fact]
    public async Task A_file_deleted_while_being_read_is_still_read_completely()
    {
        FileSystemReportArtifactStore store = new(_root);
        byte[] content = RandomNumberGenerator.GetBytes(200_000);

        StoredReportFile stored = await store.WriteAsync((stream, token) => stream.WriteAsync(content, token).AsTask(), 1_000_000);

        await using Stream? reading = await store.OpenReadAsync(stored.StorageKey);
        Assert.NotNull(reading);

        byte[] head = new byte[1000];
        await reading.ReadExactlyAsync(head);

        // Retention runs now.
        await store.DeleteAsync(stored.StorageKey);

        using MemoryStream rest = new();
        await reading.CopyToAsync(rest);

        byte[] whole = [.. head, .. rest.ToArray()];

        Assert.Equal(content, whole);
        Assert.Null(await store.OpenReadAsync(stored.StorageKey));
    }

    [Fact]
    public async Task Abandoned_partial_writes_are_swept_and_fresh_ones_are_not()
    {
        FileSystemReportArtifactStore store = new(_root);
        string incoming = Path.Combine(_root, "incoming");
        string old = Path.Combine(incoming, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.partial");
        string fresh = Path.Combine(incoming, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.partial");

        await File.WriteAllTextAsync(old, "x");
        await File.WriteAllTextAsync(fresh, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));

        Assert.Equal(1, await store.SweepAbandonedWritesAsync(DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task No_configured_root_means_no_storage_and_no_fallback()
    {
        FileSystemReportArtifactStore store = new(null);

        Assert.False(store.IsConfigured);
        await Assert.ThrowsAsync<ReportStorageUnavailableException>(
            () => store.WriteAsync((_, _) => Task.CompletedTask, 10));
    }

    [Fact]
    public void An_unsafe_root_is_refused()
    {
        string application = Path.Combine(_root, "app");
        Directory.CreateDirectory(application);

        // Relative: would resolve against whatever the working directory is.
        Assert.Throws<InvalidOperationException>(() => FileSystemReportArtifactStore.ValidateRoot("reports", application));

        // Inside the application, and so potentially inside its web root.
        Assert.Throws<InvalidOperationException>(
            () => FileSystemReportArtifactStore.ValidateRoot(Path.Combine(application, "wwwroot", "reports"), application));

        // Inside a source repository.
        string repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        Assert.Throws<InvalidOperationException>(
            () => FileSystemReportArtifactStore.ValidateRoot(Path.Combine(repository, "reports"), application));

        // Somewhere else entirely: accepted.
        string safe = Path.Combine(_root, "storage");
        Assert.Equal(safe, FileSystemReportArtifactStore.ValidateRoot(safe, application));
    }
}
