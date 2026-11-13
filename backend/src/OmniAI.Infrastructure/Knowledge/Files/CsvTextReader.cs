using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OmniAI.Application.Knowledge.Files;

namespace OmniAI.Infrastructure.Knowledge.Files;

/// <summary>
/// Renders a CSV as text a language model can read.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A spreadsheet is not knowledge until it is sentences.</strong> A grid
/// of cells chunked as-is produces fragments like <c>Basic,29,10GB</c>, which
/// retrieval cannot match against a question and a model cannot answer from. So
/// each record becomes labelled lines — <c>Plan: Basic</c> — where the column
/// name travels with the value and each record stands on its own.
/// </para>
/// <para>
/// <strong>Deterministic, because the fingerprint depends on it.</strong> The
/// same file must always render to the same text: the column order is the file's
/// own, every record is rendered the same way, and nothing is sorted, inferred or
/// formatted by locale. If this varied, re-uploading an unchanged file would look
/// like a change and buy a fresh set of embeddings.
/// </para>
/// <para>
/// <strong>Cells are text, always.</strong> A cell reading <c>=SUM(A1:A9)</c> is
/// stored as those nine characters. Nothing evaluates a formula, resolves a
/// reference or follows a link — this is a reader, and a spreadsheet engine is
/// not something this platform has or wants.
/// </para>
/// <para>
/// One delimiter: the comma. Semicolon-separated exports are common in some
/// locales and supporting both would mean guessing, which is how a file with
/// commas inside quoted fields gets silently misread. A file that is not
/// comma-separated arrives as one column per row and looks obviously wrong,
/// which is better than looking subtly wrong.
/// </para>
/// </remarks>
internal static class CsvTextReader
{
    /// <summary>Reads the records, rendering each as labelled lines.</summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The file is malformed, empty, or past one of the bounds.
    /// </exception>
    public static string Read(string csv, KnowledgeFilePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(policy);

        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,

            // A ragged row is a fact about the file, not a reason to refuse it:
            // exports routinely omit trailing empty fields. Missing values are
            // read as absent and the record still renders.
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.Trim,
        };

        using StringReader reader = new(csv);
        using CsvReader parser = new(reader, configuration);

        string[] headers = ReadHeaders(parser, policy);

        StringBuilder text = new();
        int records = 0;

        while (Advance(parser))
        {
            records++;

            if (records > policy.MaxCsvRows)
            {
                throw new KnowledgeFileRejectedException(
                    KnowledgeFileRejection.TooMuchContent,
                    KnowledgeFileRefusals.TooManyRows,
                    $"That file has more than {policy.MaxCsvRows:N0} rows. Split it into smaller "
                    + "files, or summarise it before uploading.",
                    limit: policy.MaxCsvRows,
                    limitUnit: KnowledgeFileLimitUnits.Rows);
            }

            string record = RenderRecord(parser, headers, policy);

            if (record.Length == 0)
            {
                // Every cell was blank. A spacer row in a spreadsheet, and
                // nothing worth a paragraph of its own.
                continue;
            }

            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            text.Append(record);

            if (text.Length > policy.MaxExtractedCharacters)
            {
                throw new KnowledgeFileRejectedException(
                    KnowledgeFileRejection.TooMuchContent,
                    KnowledgeFileRefusals.TooManyCharacters,
                    $"That file renders to more than {policy.MaxExtractedCharacters:N0} "
                    + "characters. Split it into smaller files.",
                    limit: policy.MaxExtractedCharacters,
                    limitUnit: KnowledgeFileLimitUnits.Characters);
            }
        }

        string extracted = text.ToString().Trim();

        return extracted.Length == 0
            ? throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.NoText,
                KnowledgeFileRefusals.NoText,
                "That file has a header row but no records in it.")
            : extracted;
    }

    /// <summary>
    /// The header row, which names every value below it.
    /// </summary>
    /// <remarks>
    /// Required. Without it there is nothing to label values with, and
    /// <c>Column 1: 29</c> tells a model no more than <c>29</c> did. A file
    /// without headers is refused with a sentence saying so, rather than rendered
    /// into something useless.
    /// </remarks>
    private static string[] ReadHeaders(CsvReader parser, KnowledgeFilePolicy policy)
    {
        if (!Advance(parser))
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.NoText,
                KnowledgeFileRefusals.Empty,
                "That file is empty.");
        }

        parser.ReadHeader();

        string[] headers = parser.HeaderRecord ?? [];

        if (headers.Length == 0 || headers.All(string.IsNullOrWhiteSpace))
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.Unreadable,
                KnowledgeFileRefusals.HeaderRequired,
                "That file needs a header row naming each column, so the values can be "
                + "understood.");
        }

        return headers.Length > policy.MaxCsvColumns
            ? throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TooMuchContent,
                KnowledgeFileRefusals.TooManyColumns,
                $"That file has {headers.Length} columns and the limit is "
                + $"{policy.MaxCsvColumns}.",
                limit: policy.MaxCsvColumns,
                limitUnit: KnowledgeFileLimitUnits.Columns)
            : headers;
    }

    /// <summary>One record, as one labelled line per non-empty column.</summary>
    private static string RenderRecord(
        CsvReader parser,
        string[] headers,
        KnowledgeFilePolicy policy)
    {
        StringBuilder record = new();

        for (int column = 0; column < headers.Length; column++)
        {
            string header = headers[column];

            if (string.IsNullOrWhiteSpace(header))
            {
                // An unnamed column has nothing to label its values with.
                continue;
            }

            if (!parser.TryGetField(column, out string? value) || string.IsNullOrWhiteSpace(value))
            {
                // An empty cell says nothing, and "Plan:" with nothing after it
                // would read as a fact about the record that is not one.
                continue;
            }

            if (value.Length > policy.MaxCsvCellCharacters)
            {
                throw new KnowledgeFileRejectedException(
                    KnowledgeFileRejection.TooMuchContent,
                    KnowledgeFileRefusals.CellTooLong,
                    $"A cell in that file holds more than {policy.MaxCsvCellCharacters:N0} "
                    + "characters. It is too large to read as a table.",
                    limit: policy.MaxCsvCellCharacters,
                    limitUnit: KnowledgeFileLimitUnits.Characters);
            }

            if (record.Length > 0)
            {
                record.Append('\n');
            }

            // Newlines inside a quoted cell are folded to spaces, so one record
            // stays one block: a cell containing a paragraph must not look like
            // the start of the next record.
            record
                .Append(header.Trim())
                .Append(": ")
                .Append(value.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }

        return record.ToString();
    }

    /// <summary>Moves to the next row, turning a parse failure into one sentence.</summary>
    private static bool Advance(CsvReader parser)
    {
        try
        {
            return parser.Read();
        }
        catch (CsvHelperException)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.Unreadable,
                KnowledgeFileRefusals.Damaged,
                "That file could not be read as CSV. Check that quotes and commas are balanced.");
        }
    }
}
