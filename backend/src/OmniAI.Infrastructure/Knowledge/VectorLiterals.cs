using System.Globalization;
using System.Text;
using OmniAI.Application.Knowledge;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// Renders a vector in the text form pgvector accepts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The column is a real <c>vector</c>, not text.</strong> What this
/// produces is a parameter value that the database casts to <c>vector</c> on
/// arrival -- the same representation <c>psql</c> itself sends. Nothing is stored
/// as JSON, as base64 or as an array of text: the column type is
/// <c>vector(1536)</c>, which is what makes the HNSW index and the distance
/// operators work at all.
/// </para>
/// <para>
/// <strong>It is always a parameter.</strong> The rendered string is bound, never
/// concatenated into SQL. It contains only digits, signs, dots, exponents,
/// commas and brackets by construction, but that is not the reason it is safe --
/// being a parameter is.
/// </para>
/// <para>
/// <strong>Round-trip format, and invariant culture.</strong> "R" is what
/// guarantees the float that comes back is the float that went in; a
/// culture-sensitive conversion would write "0,5" on a machine configured for
/// most of Europe and produce a vector with twice as many elements as it should
/// have. This solution runs with invariant globalisation, so that cannot happen
/// today -- the explicit culture is here so it still cannot happen if that ever
/// changes.
/// </para>
/// </remarks>
internal static class VectorLiterals
{
    public static string Render(EmbeddingVector vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        ReadOnlySpan<float> values = vector.Span;

        // Twelve characters per value is generous for the round-trip form of a
        // normalised embedding component, plus the brackets and separators.
        StringBuilder builder = new((values.Length * 12) + 2);

        builder.Append('[');

        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }

        builder.Append(']');

        return builder.ToString();
    }
}
