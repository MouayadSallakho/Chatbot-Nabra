using System.Security.Cryptography;
using System.Text;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// The text of a knowledge document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Semi-static business information, and only that.</strong> Opening
/// hours, a returns policy, what is and is not included in a plan, how a service
/// works. It is not a place to put live operational data: a price that changes
/// hourly or an order's status would be answered from whatever was true when
/// somebody last pressed Index, which is worse than not answering at all. Live
/// data will be read through explicit authorised tools, not from here.
/// </para>
/// <para>
/// <strong>The bound is 100,000 characters, and nothing is ever truncated.</strong>
/// Roughly a short book, and far more than any single policy document a business
/// writes. Something larger is not one document -- it is a manual, and splitting
/// it is a decision for the person who wrote it rather than for a silent cut that
/// would drop the end of their text without telling them.
/// </para>
/// <para>
/// <strong>Line endings are normalised to a single form.</strong> The same
/// paragraph pasted from Windows and from a Mac must produce the same
/// fingerprint, the same chunks and therefore the same embeddings; if it did not,
/// re-pasting identical text would silently spend money re-indexing something
/// that had not changed.
/// </para>
/// </remarks>
public sealed record KnowledgeContent
{
    public const int MaxLength = 100_000;

    private KnowledgeContent(string value) => Value = value;

    /// <summary>The normalised text: trimmed, with line endings folded to "\n".</summary>
    public string Value { get; }

    public static KnowledgeContent Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A knowledge document needs some content.");
        }

        // Order matters: fold first, then trim, so a document ending in "\r\n"
        // does not keep a stray carriage return that would change its fingerprint.
        string normalised = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalised.Length == 0)
        {
            throw new DomainValidationException("A knowledge document needs some content.");
        }

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A knowledge document must be at most {MaxLength:N0} characters; " +
                $"the supplied content has {normalised.Length:N0}. It is not truncated. " +
                "Split it into separate documents instead.");
        }

        foreach (char character in normalised)
        {
            // Tabs and newlines are ordinary formatting in a written document.
            // Everything else in the control range carries no meaning here and
            // would corrupt logs, editors and anything that renders the text.
            if (char.IsControl(character) && character is not ('\n' or '\t'))
            {
                throw new DomainValidationException(
                    "A knowledge document must not contain control characters other than "
                    + "tab and line feed.");
            }
        }

        return new KnowledgeContent(normalised);
    }

    /// <summary>
    /// A deterministic fingerprint of this exact text.
    /// </summary>
    /// <remarks>
    /// SHA-256 over the UTF-8 bytes of the normalised value. It is not a security
    /// device and guards no secret -- it exists so that "has this document
    /// changed since it was indexed?" is answered by comparing 32 bytes rather
    /// than by comparing a hundred thousand characters, and so that re-indexing
    /// unchanged text can be recognised and refused before a single paid
    /// embedding call is made.
    /// </remarks>
    public ContentFingerprint Fingerprint() =>
        ContentFingerprint.From(SHA256.HashData(Encoding.UTF8.GetBytes(Value)));

    public override string ToString() => Value;
}
