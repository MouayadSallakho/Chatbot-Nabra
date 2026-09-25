using System.Security.Cryptography;
using System.Text.RegularExpressions;
using OmniAI.Application.Reports;

namespace OmniAI.Infrastructure.Reports;

/// <summary>Where the filesystem artifact store keeps its files.</summary>
public sealed class ReportStorageOptions
{
    public const string Section = "Reports:Storage";

    /// <summary>
    /// An absolute directory outside the application and any web root. Empty
    /// means reports cannot be generated on this deployment.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Report files on a local disk, for a single-node deployment.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Keys, never paths.</strong> A key is 32 lower-case hex characters the
/// server generated. It is checked against that shape before it is ever joined to
/// a path, and the joined path is checked again to be inside the root, so no key
/// — stored, forged or corrupted — can name a file anywhere else.
/// </para>
/// <para>
/// <strong>Temporary, then final, by rename.</strong> Content is written to a
/// <c>.partial</c> file in a separate directory, flushed to disk, measured and
/// hashed as it goes, and only then moved under its key. A rename within one
/// volume is atomic, so a reader sees either no file or the whole file. Anything
/// that fails deletes the temporary file.
/// </para>
/// <para>
/// <strong>Deleting under a reader is safe.</strong> Files are opened with
/// <see cref="FileShare.Delete"/>, so retention may remove a file while a
/// download is streaming it: the open handle keeps reading the complete bytes, and
/// the name is gone for everybody after.
/// </para>
/// <para>
/// <strong>Replaceable.</strong> An object-store implementation of
/// <see cref="IReportArtifactStore"/> needs no change anywhere else.
/// </para>
/// </remarks>
internal sealed partial class FileSystemReportArtifactStore : IReportArtifactStore
{
    private const string FinalDirectory = "artifacts";
    private const string TemporaryDirectory = "incoming";
    private const string TemporarySuffix = ".partial";
    private const int BufferSize = 81_920;

    private readonly string? _final;
    private readonly string? _temporary;

    /// <param name="rootPath">Already validated by <see cref="ValidateRoot"/>, or empty.</param>
    public FileSystemReportArtifactStore(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        _final = Path.Combine(rootPath, FinalDirectory);
        _temporary = Path.Combine(rootPath, TemporaryDirectory);

        Directory.CreateDirectory(_final);
        Directory.CreateDirectory(_temporary);
    }

    public bool IsConfigured => _final is not null;

    /// <summary>
    /// Refuses a root this platform must never write reports into.
    /// </summary>
    /// <remarks>
    /// Relative paths are refused because they resolve against whatever the
    /// working directory happens to be. The application's own directory — and so
    /// its <c>wwwroot</c> — is refused because files there can end up served
    /// statically. The repository is refused because a generated report must never
    /// be one <c>git add</c> away from being committed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The root is unsafe.</exception>
    public static string ValidateRoot(string rootPath, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(contentRoot);

        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new InvalidOperationException(
                $"{ReportStorageOptions.Section}:RootPath must be an absolute path.");
        }

        string root = Normalise(rootPath);
        string application = Normalise(contentRoot);

        if (IsWithin(root, application) || IsWithin(application, root))
        {
            throw new InvalidOperationException(
                $"{ReportStorageOptions.Section}:RootPath must be outside the application directory.");
        }

        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                throw new InvalidOperationException(
                    $"{ReportStorageOptions.Section}:RootPath must not be inside a source repository.");
            }
        }

        return root;
    }

    public async Task<StoredReportFile> WriteAsync(
        Func<Stream, CancellationToken, Task> write,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        if (_final is null || _temporary is null)
        {
            throw new ReportStorageUnavailableException();
        }

        string key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string temporary = Path.Combine(_temporary, key + TemporarySuffix);
        string final = PathFor(key);

        try
        {
            long length;
            string hash;

            FileStream file = new(
                temporary,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                    BufferSize = BufferSize,
                });

            await using (file.ConfigureAwait(false))
            {
                using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                BoundedHashingStream bounded = new(file, sha, maxBytes);

                await using (bounded.ConfigureAwait(false))
                {
                    await write(bounded, cancellationToken).ConfigureAwait(false);
                    await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // On the disk, not in a cache, before it gets a name anybody can read.
                file.Flush(flushToDisk: true);

                length = bounded.Written;
                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            if (length == 0)
            {
                throw new InvalidOperationException("A report renderer produced no bytes.");
            }

            File.Move(temporary, final, overwrite: false);

            return new StoredReportFile(key, length, hash);
        }
        catch
        {
            TryDelete(temporary);

            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (_final is null || !IsKey(storageKey))
        {
            return Task.FromResult<Stream?>(null);
        }

        try
        {
            Stream stream = new FileStream(
                PathFor(storageKey),
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = BufferSize,
                });

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (_final is not null && IsKey(storageKey))
        {
            // File.Delete does nothing for a file that is already gone.
            File.Delete(PathFor(storageKey));
        }

        return Task.CompletedTask;
    }

    public Task<int> SweepAbandonedWritesAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        if (_temporary is null)
        {
            return Task.FromResult(0);
        }

        int removed = 0;

        foreach (string path in Directory.EnumerateFiles(_temporary, "*" + TemporarySuffix))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime && TryDelete(path))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<string>> ListFinalizedAsync(
        DateTimeOffset finalizedBefore,
        string? afterKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (_final is null)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        List<string> keys = [];

        foreach (string path in Directory.EnumerateFiles(_final))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(path);

            // The store's own key shape only, and never a symbolic link: nothing
            // else in this directory was written by this store.
            if (!IsKey(name)
                || (afterKey is not null && string.CompareOrdinal(name, afterKey) <= 0)
                || new FileInfo(path).LinkTarget is not null
                || File.GetLastWriteTimeUtc(path) >= finalizedBefore.UtcDateTime)
            {
                continue;
            }

            keys.Add(name);
        }

        keys.Sort(StringComparer.Ordinal);

        return Task.FromResult<IReadOnlyList<string>>([.. keys.Take(limit)]);
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_final is not null && IsKey(storageKey) && File.Exists(PathFor(storageKey)));

    private string PathFor(string key)
    {
        string path = Path.GetFullPath(Path.Combine(_final!, key));

        // Belt and braces: the key's shape already rules this out.
        if (!IsWithin(path, _final!))
        {
            throw new InvalidOperationException("A report storage key resolved outside the report store.");
        }

        return path;
    }

    private static bool IsKey(string? key) => key is not null && KeyShape().IsMatch(key);

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Normalise(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsWithin(string path, string directory)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string normalisedDirectory = Normalise(directory);
        string normalisedPath = Normalise(path);

        return normalisedPath.Equals(normalisedDirectory, comparison)
            || normalisedPath.StartsWith(normalisedDirectory + Path.DirectorySeparatorChar, comparison);
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyShape();

    /// <summary>
    /// Counts and hashes what passes through, and refuses to pass more than the limit.
    /// </summary>
    private sealed class BoundedHashingStream(Stream inner, IncrementalHash hash, long maxBytes) : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written;

        public override long Position
        {
            get => Written;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            hash.AppendData(buffer);
            inner.Write(buffer);
            Written += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Admit(buffer.Length);
            hash.AppendData(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            Written += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Admit(int count)
        {
            if (Written + count > maxBytes)
            {
                throw new ReportTooLargeException();
            }
        }
    }
}
