namespace OmniAI.Domain.Knowledge;

/// <summary>
/// The kinds of file a workspace may upload into Knowledge.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A closed set, and deliberately short.</strong> Every member here is a
/// format this platform can turn into plain text without running anything: no
/// macros, no embedded objects, no image recognition. A format that needs one of
/// those is not a missing enum member — it is a decision somebody has to make.
/// </para>
/// <para>
/// The enum is the Domain's whole vocabulary for file kinds. It names no parser,
/// no library and no media type; those live in Infrastructure, where the choice
/// of one PDF library over another can change without the Domain noticing.
/// </para>
/// </remarks>
public enum KnowledgeFileType
{
    /// <summary>A text-bearing PDF. Scanned pages are not read; there is no OCR.</summary>
    Pdf = 1,

    /// <summary>A Word document, read as paragraphs of text.</summary>
    Docx = 2,

    /// <summary>A plain text file, UTF-8.</summary>
    Text = 3,

    /// <summary>Comma-separated records, rendered deterministically as text.</summary>
    Csv = 4,
}
