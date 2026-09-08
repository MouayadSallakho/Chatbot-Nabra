namespace OmniAI.Application.Knowledge.ListKnowledgeDocuments;

/// <summary>One page of a workspace's knowledge documents.</summary>
public sealed record KnowledgeDocumentPage(
    IReadOnlyList<KnowledgeDocumentSummary> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public static KnowledgeDocumentPage Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>A page or page size outside what the list will serve.</summary>
public sealed class KnowledgeDocumentPageValidationException : Exception
{
    public KnowledgeDocumentPageValidationException()
        : base("The requested page is not valid.")
    {
    }

    public KnowledgeDocumentPageValidationException(string message)
        : base(message)
    {
    }

    public KnowledgeDocumentPageValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static KnowledgeDocumentPageValidationException ForPage(int page) =>
        new($"Page must be {KnowledgeDocumentPage.DefaultPage} or greater; {page} was requested.");

    public static KnowledgeDocumentPageValidationException ForPageSize(int pageSize) =>
        new(
            $"Page size must be between 1 and {KnowledgeDocumentPage.MaxPageSize}; "
            + $"{pageSize} was requested.");
}
