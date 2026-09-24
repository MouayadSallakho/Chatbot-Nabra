using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;

namespace OmniAI.Infrastructure.Reports.Rendering;

/// <summary>
/// A report's primary table as RFC 4180 CSV.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A real CSV writer.</strong> CsvHelper quotes fields with commas,
/// quotes and line breaks correctly; a <c>string.Join(",")</c> would corrupt the
/// first customer name with a comma in it.
/// </para>
/// <para>
/// <strong>Spreadsheet formula injection is neutralised.</strong> A text value
/// that begins with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage
/// return is prefixed with an apostrophe, the OWASP-recommended defence, so Excel
/// and LibreOffice show it as text instead of evaluating it. Only text cells are
/// touched: every number in a report is written by this platform and is never
/// negative, and a label a customer typed is always text.
/// </para>
/// <para>
/// <strong>UTF-8 with a byte-order mark, deliberately.</strong> Without it,
/// Excel on Windows opens a UTF-8 file as the local ANSI code page and turns every
/// non-Latin name — Arabic, among others — into mojibake. The BOM is the one
/// signal Excel honours, and every other CSV consumer ignores it.
/// </para>
/// </remarks>
internal sealed class CsvReportRenderer : IReportRenderer
{
    private static readonly char[] FormulaPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    public ReportFormat Format => ReportFormat.Csv;

    public async Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        ReportTable table = document.Primary;

        StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 16_384, leaveOpen: true);

        await using (writer.ConfigureAwait(false))
        {
            CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
            {
                // The escaping below is this renderer's own, so it can be tested
                // exactly and applied to text alone.
                InjectionOptions = InjectionOptions.None,
                NewLine = "\r\n",
            };

            CsvWriter csv = new(writer, configuration, leaveOpen: true);

            await using (csv.ConfigureAwait(false))
            {
                foreach (ReportColumn column in table.Columns)
                {
                    csv.WriteField(Neutralise(column.Header));
                }

                await csv.NextRecordAsync().ConfigureAwait(false);

                foreach (IReadOnlyList<ReportValue> row in table.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (ReportValue value in row)
                    {
                        csv.WriteField(Cell(value));
                    }

                    await csv.NextRecordAsync().ConfigureAwait(false);
                }

                await csv.FlushAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// A text value a spreadsheet will not evaluate.
    /// </summary>
    internal static string Neutralise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
    }

    private static string Cell(ReportValue value) => value.Kind switch
    {
        ReportValueKind.Text => Neutralise(value.TextValue),
        ReportValueKind.Count => value.CountValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Money => value.MoneyValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Date => value.DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Instant =>
            value.InstantValue?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            ?? string.Empty,
        _ => string.Empty,
    };
}
