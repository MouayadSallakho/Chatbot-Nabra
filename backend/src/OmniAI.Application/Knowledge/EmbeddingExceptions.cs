namespace OmniAI.Application.Knowledge;

/// <summary>
/// No embedding provider could serve the request.
/// </summary>
/// <remarks>
/// Deliberately its own type rather than the text-generation equivalent. They
/// become the same status code, but they are different capabilities that fail for
/// different reasons and are read by different people: an operator seeing
/// "embeddings are unavailable" knows indexing and knowledge retrieval are
/// affected and that chat without knowledge is not.
/// </remarks>
public sealed class EmbeddingUnavailableException : Exception
{
    private const string Default =
        "Knowledge indexing is not available on this deployment because no embedding "
        + "provider is configured. Nothing was indexed and no content was changed.";

    public EmbeddingUnavailableException()
        : base(Default)
    {
    }

    public EmbeddingUnavailableException(string message)
        : base(message)
    {
    }

    public EmbeddingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The embedding provider did not answer in time.</summary>
public sealed class EmbeddingTimeoutException : Exception
{
    private const string Default =
        "The embedding provider did not respond in time. Nothing was indexed.";

    public EmbeddingTimeoutException()
        : base(Default)
    {
    }

    public EmbeddingTimeoutException(string message)
        : base(message)
    {
    }

    public EmbeddingTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The embedding provider answered with something this platform cannot use.
/// </summary>
/// <remarks>
/// A wrong number of vectors, a wrong number of dimensions, or a value that is
/// not a finite number. Raised rather than repaired: there is no honest way to
/// guess what a missing or malformed vector should have been, and storing one
/// would corrupt every future search silently.
/// </remarks>
public sealed class EmbeddingResponseInvalidException : Exception
{
    private const string Default =
        "The embedding provider returned a response this platform could not use. "
        + "Nothing was indexed.";

    public EmbeddingResponseInvalidException()
        : base(Default)
    {
    }

    public EmbeddingResponseInvalidException(string message)
        : base(message)
    {
    }

    public EmbeddingResponseInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
