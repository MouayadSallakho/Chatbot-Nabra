using System.Globalization;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// One embedding, validated at the boundary where it arrives.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A provider response is not trusted to be well formed.</strong> Every
/// vector that enters this platform is checked here for the three failures that
/// are otherwise silent: the wrong number of dimensions, a <c>NaN</c>, and an
/// infinity. None of them throws when it is stored, and none of them throws when
/// it is compared -- they simply poison the arithmetic. A single <c>NaN</c> in a
/// vector makes every distance involving it <c>NaN</c>, which sorts unpredictably
/// and would quietly move a document to the top or bottom of every search
/// forever.
/// </para>
/// <para>
/// <strong>The dimension check is not a formality either.</strong> Storing a
/// 3,072-dimension vector in a column declared for 1,536 would be refused by
/// PostgreSQL with a message about a column type, arriving from deep inside a
/// transaction. Catching it here says what actually happened: the provider
/// answered with something other than what this deployment is configured for.
/// </para>
/// <para>
/// The values are copied on the way in and on the way out, so nothing outside can
/// mutate a vector that has already been validated.
/// </para>
/// </remarks>
public sealed class EmbeddingVector
{
    private readonly float[] _values;

    private EmbeddingVector(float[] values) => _values = values;

    public int Dimensions => _values.Length;

    /// <summary>The values, for the adapter that has to write them.</summary>
    public ReadOnlySpan<float> Span => _values;

    /// <summary>Validates a vector a provider returned.</summary>
    /// <exception cref="EmbeddingResponseInvalidException">
    /// The shape or the numbers are unusable.
    /// </exception>
    public static EmbeddingVector Create(IReadOnlyList<float>? values, int expectedDimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDimensions);

        if (values is null)
        {
            throw new EmbeddingResponseInvalidException(
                "The embedding provider returned no vector for an input it was given.");
        }

        if (values.Count != expectedDimensions)
        {
            throw new EmbeddingResponseInvalidException(string.Create(
                CultureInfo.InvariantCulture,
                $"The embedding provider returned a vector of {values.Count} dimensions where this deployment is configured for {expectedDimensions}. Nothing was stored: vectors of different widths cannot be compared."));
        }

        float[] copy = new float[values.Count];

        for (int index = 0; index < values.Count; index++)
        {
            float value = values[index];

            if (!float.IsFinite(value))
            {
                throw new EmbeddingResponseInvalidException(
                    "The embedding provider returned a vector containing a value that is not a "
                    + "finite number. Nothing was stored: a single such value would make every "
                    + "distance computed against this vector meaningless.");
            }

            copy[index] = value;
        }

        return new EmbeddingVector(copy);
    }

    /// <summary>A copy of the values.</summary>
    public float[] ToArray() => [.. _values];
}
