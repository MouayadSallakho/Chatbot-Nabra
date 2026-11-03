using System.Globalization;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// The name of an uploaded file, as a person recognises it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Display metadata, and nothing else.</strong> Nothing on this platform
/// opens a file, writes one, or joins this value to a path — the uploaded bytes
/// are read from the request and discarded when it ends. That is what makes this
/// type safe to keep at all, and it is why the sanitising below is about what a
/// person will read rather than about what a filesystem would do.
/// </para>
/// <para>
/// <strong>Path segments are stripped rather than rejected.</strong> Browsers
/// disagree about whether they send <c>report.pdf</c> or
/// <c>C:\Users\someone\report.pdf</c>, and a directory somebody happens to work
/// in is not a reason to refuse their upload — nor is it something this platform
/// should store. The last segment is kept and the rest is dropped, so
/// <c>../../etc/passwd</c> and <c>..\..\secrets.txt</c> both become an ordinary
/// harmless name.
/// </para>
/// <para>
/// Control characters are removed, because this string is rendered in the admin
/// and written to the database, and a name containing a line break or a
/// terminal escape is a name designed to be rendered somewhere it should not be.
/// </para>
/// </remarks>
public sealed record SourceFileName
{
    /// <summary>
    /// The longest name stored.
    /// </summary>
    /// <remarks>
    /// Generous for a real filename and far short of anything that could be used
    /// to smuggle a payload through a display field.
    /// </remarks>
    public const int MaxLength = 255;

    private SourceFileName(string value) => Value = value;

    /// <summary>The sanitised name: one segment, no control characters, trimmed.</summary>
    public string Value { get; }

    /// <summary>
    /// Takes whatever the browser sent and produces something safe to store.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// Nothing usable survived the sanitising, or what did was too long.
    /// </exception>
    public static SourceFileName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("An uploaded file needs a name.")
            {
                Code = "knowledge.file.name.required",
            };
        }

        // The last segment of whatever separator convention the client used.
        // Taking the last one is what makes a traversal attempt collapse into an
        // ordinary name rather than being refused with a message that tells the
        // sender their attempt was noticed.
        string candidate = value;

        int separator = candidate.LastIndexOfAny(['/', '\\']);

        if (separator >= 0)
        {
            candidate = candidate[(separator + 1)..];
        }

        // Colons would carry an alternate data stream on Windows and a drive
        // prefix anywhere else; neither belongs in a display name.
        candidate = candidate.Replace(':', '_');

        string sanitised = new([.. candidate.Where(character => !char.IsControl(character))]);

        sanitised = sanitised.Trim().Trim('.');

        if (sanitised.Length == 0)
        {
            throw new DomainValidationException(
                "That file name could not be read. Rename the file and try again.")
            {
                Code = "knowledge.file.name.unreadable",
            };
        }

        if (sanitised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A file name must be at most {MaxLength} characters.")
            {
                Code = "knowledge.file.name.too_long",
            };
        }

        return new SourceFileName(sanitised);
    }

    /// <summary>
    /// The name without its extension, for use as a document title.
    /// </summary>
    /// <remarks>
    /// Only the final extension is removed, so <c>tariffs.2026.csv</c> becomes
    /// <c>tariffs.2026</c> rather than <c>tariffs</c>. A name that is nothing but
    /// an extension keeps it, because the alternative is an empty title.
    /// </remarks>
    public string WithoutExtension()
    {
        int dot = Value.LastIndexOf('.');

        return dot > 0 ? Value[..dot] : Value;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
