using OmniAI.Application.Knowledge;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// The three provider failures that are otherwise completely silent.
/// </summary>
public sealed class EmbeddingVectorTests
{
    [Fact]
    public void A_well_formed_vector_is_accepted()
    {
        EmbeddingVector vector = EmbeddingVector.Create([0.1f, -0.2f, 0.3f], 3);

        Assert.Equal(3, vector.Dimensions);
        Assert.Equal([0.1f, -0.2f, 0.3f], vector.ToArray());
    }

    [Fact]
    public void A_vector_of_the_wrong_width_is_refused()
    {
        // Storing a 3,072-wide vector in a column declared for 1,536 would be
        // refused by PostgreSQL with a message about a column type, from deep
        // inside a transaction. Catching it here says what actually happened.
        EmbeddingResponseInvalidException failure =
            Assert.Throws<EmbeddingResponseInvalidException>(
                () => EmbeddingVector.Create([0.1f, 0.2f], 3));

        Assert.Contains("2 dimensions", failure.Message, StringComparison.Ordinal);
        Assert.Contains("configured for 3", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vector_containing_a_nan_is_refused()
    {
        // A single NaN makes every distance involving this vector NaN, which sorts
        // unpredictably. The document would silently sit at the top or the bottom
        // of every search forever, and nothing would report an error.
        Assert.Throws<EmbeddingResponseInvalidException>(
            () => EmbeddingVector.Create([0.1f, float.NaN, 0.3f], 3));
    }

    [Fact]
    public void A_vector_containing_an_infinity_is_refused()
    {
        Assert.Throws<EmbeddingResponseInvalidException>(
            () => EmbeddingVector.Create([0.1f, float.PositiveInfinity, 0.3f], 3));

        Assert.Throws<EmbeddingResponseInvalidException>(
            () => EmbeddingVector.Create([0.1f, float.NegativeInfinity, 0.3f], 3));
    }

    [Fact]
    public void A_missing_vector_is_refused()
    {
        Assert.Throws<EmbeddingResponseInvalidException>(() => EmbeddingVector.Create(null, 3));
    }

    [Fact]
    public void The_values_are_copied_so_a_validated_vector_cannot_be_altered()
    {
        float[] values = [0.1f, 0.2f, 0.3f];

        EmbeddingVector vector = EmbeddingVector.Create(values, 3);

        values[0] = float.NaN;

        Assert.Equal(0.1f, vector.ToArray()[0]);
    }
}
