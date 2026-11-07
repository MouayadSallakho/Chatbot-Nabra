using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// English report files are exactly what they were before Phase 37.7.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Captured before anything changed.</strong> The files under
/// <c>Reports/Golden/en</c> were written by this test, in write mode, from the
/// 37.6 code (commit <c>2b5824a</c>), before Phase 37.7 touched the builder or a
/// renderer. From then on the test compares: every report type, in every format,
/// built in English from <see cref="ReportFixtures"/>, must reproduce them.
/// </para>
/// <para>
/// <strong>What "exactly" means per format.</strong> A CSV is compared byte for
/// byte. A workbook is a ZIP whose entries carry the time they were written, so
/// it is compared part by part: every part's name and its full XML, with the
/// SDK's random relationship ids numbered in order of appearance. A PDF is
/// compared byte for byte except its <c>/ID</c>, which Skia derives from the
/// clock; everything a reader sees, the text layer, the fonts and the metadata,
/// is in the compared bytes.
/// </para>
/// <para>
/// Write mode (<c>OMNIAI_WRITE_REPORT_GOLDEN=1</c>) only ever ran once, to
/// capture; it is not a way to make a failing comparison pass.
/// </para>
/// </remarks>
public sealed partial class ReportGoldenFileTests
{
    public static TheoryData<ReportType, ReportFormat> Cases()
    {
        TheoryData<ReportType, ReportFormat> cases = [];

        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            foreach (ReportFormat format in Enum.GetValues<ReportFormat>())
            {
                cases.Add(type, format);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task English_files_are_identical_to_the_files_before_phase_37_7(ReportType type, ReportFormat format)
    {
        byte[] canonical = await CanonicalAsync(type, format);

        string name = GoldenName(type, format);

        if (Environment.GetEnvironmentVariable("OMNIAI_WRITE_REPORT_GOLDEN") == "1")
        {
            string folder = Path.Combine(SourceFolder(), "Golden", "en");
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(Path.Combine(folder, name), canonical);

            return;
        }

        byte[] golden = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Reports", "Golden", "en", name));

        if (!golden.AsSpan().SequenceEqual(canonical))
        {
            Assert.Fail($"{name} differs from its golden file (first difference at byte {FirstDifference(golden, canonical)}).");
        }
    }

    /// <summary>Two renders of one document are one file, so the comparison above means something.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Rendering_is_deterministic_once_the_clock_derived_parts_are_set_aside(ReportType type, ReportFormat format)
    {
        Assert.Equal(await CanonicalAsync(type, format), await CanonicalAsync(type, format));
    }

    internal static async Task<byte[]> CanonicalAsync(ReportType type, ReportFormat format)
    {
        ReportDocument document = await ReportFixtures.BuildAsync(type);
        byte[] bytes = await ReportRenderingTests.Render(ReportFixtures.Renderer(format), document);

        return Canonical(format, bytes);
    }

    internal static byte[] Canonical(ReportFormat format, byte[] bytes) => format switch
    {
        ReportFormat.Csv => bytes,
        ReportFormat.Xlsx => Encoding.UTF8.GetBytes(Parts(bytes)),
        ReportFormat.Pdf => Encoding.Latin1.GetBytes(HashLargeStreams(PdfId().Replace(Encoding.Latin1.GetString(bytes), "/ID [<id> <id>]"))),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>
    /// The PDF with every stream over 32 KiB replaced by its SHA-256.
    /// </summary>
    /// <remarks>
    /// Those are the embedded fonts, about 0.7 MB each and identical in every
    /// report. Replacing them by their digest keeps the comparison exact (a changed
    /// byte changes the digest) and keeps eight golden PDFs from adding six
    /// megabytes to the repository. Page content streams are far smaller and stay
    /// byte for byte.
    /// </remarks>
    private static string HashLargeStreams(string pdf)
    {
        StringBuilder result = new(pdf.Length / 8);
        int position = 0;

        while (true)
        {
            int start = pdf.IndexOf("stream\n", position, StringComparison.Ordinal);

            if (start < 0 || (start > 0 && pdf[start - 1] == 'd'))
            {
                if (start < 0)
                {
                    break;
                }

                // "endstream\n": not the start of a stream.
                result.Append(pdf, position, start + 7 - position);
                position = start + 7;

                continue;
            }

            int body = start + "stream\n".Length;
            int end = pdf.IndexOf("endstream", body, StringComparison.Ordinal);

            if (end < 0)
            {
                break;
            }

            result.Append(pdf, position, body - position);

            if (end - body > 32 * 1024)
            {
                byte[] digest = System.Security.Cryptography.SHA256.HashData(Encoding.Latin1.GetBytes(pdf[body..end]));
                result.Append("[sha256 ").Append(Convert.ToHexStringLower(digest)).Append(", ")
                    .Append(end - body).Append(" bytes]\n");
            }
            else
            {
                result.Append(pdf, body, end - body);
            }

            position = end;
        }

        result.Append(pdf, position, pdf.Length - position);

        return result.ToString();
    }

    internal static string GoldenName(ReportType type, ReportFormat format) =>
        ReportCatalog.For(type).Slug + format switch
        {
            ReportFormat.Csv => ".csv",
            ReportFormat.Xlsx => ".xlsx.parts.txt",
            ReportFormat.Pdf => ".pdf.canonical",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static string Parts(byte[] workbook)
    {
        using MemoryStream stream = new(workbook);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read);

        StringBuilder parts = new();

        foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            using StreamReader reader = new(entry.Open(), Encoding.UTF8);

            parts.Append("=== ").Append(entry.FullName).Append('\n').Append(reader.ReadToEnd()).Append('\n');
        }

        // The SDK names each relationship with a random id; number them in order of
        // first appearance instead, which keeps every reference exact.
        Dictionary<string, string> ids = new(StringComparer.Ordinal);

        return RelationshipId().Replace(
            parts.ToString(),
            match =>
            {
                if (!ids.TryGetValue(match.Value, out string? stable))
                {
                    stable = "rel" + (ids.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ids[match.Value] = stable;
                }

                return stable;
            });
    }

    private static int FirstDifference(byte[] left, byte[] right)
    {
        int length = Math.Min(left.Length, right.Length);

        for (int index = 0; index < length; index++)
        {
            if (left[index] != right[index])
            {
                return index;
            }
        }

        return length;
    }

    private static string SourceFolder([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [GeneratedRegex(@"\bR[0-9a-f]{16}\b")]
    private static partial Regex RelationshipId();

    [GeneratedRegex(@"/ID \[<[0-9A-Fa-f]*> ?<[0-9A-Fa-f]*>\]")]
    private static partial Regex PdfId();
}
