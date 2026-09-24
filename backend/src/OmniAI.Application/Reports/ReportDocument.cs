using OmniAI.Domain.Reports;

namespace OmniAI.Application.Reports;

/// <summary>What kind of value a column holds, which decides how each format writes it.</summary>
public enum ReportValueKind
{
    Text = 1,

    /// <summary>A whole number of things: calls, messages, tokens.</summary>
    Count = 2,

    /// <summary>A monetary amount. Always beside a currency column; never summed across one.</summary>
    Money = 3,
    Date = 4,
    Instant = 5,
}

/// <summary>
/// One cell.
/// </summary>
/// <remarks>
/// Typed rather than pre-formatted, so the spreadsheet gets a real number and a
/// real date and the CSV gets an invariant rendering — and so text is always
/// known to be text, which is what the formula-injection defence keys on.
/// </remarks>
public sealed record ReportValue
{
    private ReportValue(ReportValueKind kind, string? text, long? count, decimal? money, DateOnly? date, DateTimeOffset? instant)
    {
        Kind = kind;
        TextValue = text;
        CountValue = count;
        MoneyValue = money;
        DateValue = date;
        InstantValue = instant;
    }

    public ReportValueKind Kind { get; }

    public string? TextValue { get; }

    public long? CountValue { get; }

    public decimal? MoneyValue { get; }

    public DateOnly? DateValue { get; }

    public DateTimeOffset? InstantValue { get; }

    public bool IsEmpty =>
        TextValue is null && CountValue is null && MoneyValue is null && DateValue is null && InstantValue is null;

    public static ReportValue Text(string? value) => new(ReportValueKind.Text, value, null, null, null, null);

    public static ReportValue Count(long value) => new(ReportValueKind.Count, null, value, null, null, null);

    public static ReportValue Money(decimal? value) => new(ReportValueKind.Money, null, null, value, null, null);

    public static ReportValue Date(DateOnly value) => new(ReportValueKind.Date, null, null, null, value, null);

    public static ReportValue Instant(DateTimeOffset? value) =>
        new(ReportValueKind.Instant, null, null, null, null, value?.ToUniversalTime());
}

/// <summary>One column heading and the kind of value beneath it.</summary>
public sealed record ReportColumn(string Header, ReportValueKind Kind);

/// <summary>A labelled figure in a report's summary.</summary>
public sealed record ReportFact(string Label, ReportValue Value);

/// <summary>
/// One table.
/// </summary>
/// <param name="Name">A short, fixed name: an XLSX sheet name. Never user text.</param>
public sealed record ReportTable(
    string Name,
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<ReportValue>> Rows);

/// <summary>
/// A report, independent of the file it will be written as.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Format-neutral on purpose.</strong> The builders decide what a report
/// says; the renderers decide only how CSV, XLSX and PDF spell it. That split is
/// what keeps the three formats of one report saying the same thing.
/// </para>
/// <para>
/// <strong>The first table is the primary one.</strong> A CSV holds exactly one
/// table, so it holds this one; the spreadsheet and the PDF hold the summary and
/// every table.
/// </para>
/// </remarks>
public sealed record ReportDocument(
    ReportType Type,
    string Title,
    string? WorkspaceName,
    ReportPeriod Period,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportFact> Summary,
    IReadOnlyList<ReportTable> Tables,
    IReadOnlyList<string> Notes)
{
    public ReportTable Primary => Tables[0];

    /// <summary>Every table row in the document: what the export bound is measured against.</summary>
    public int RowCount => Tables.Sum(table => table.Rows.Count);
}
