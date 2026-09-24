using System.Globalization;

namespace OmniAI.Domain.Reports;

/// <summary>One requested report generation.</summary>
public readonly record struct ReportExecutionId
{
    private ReportExecutionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReportExecutionId New() => new(Guid.CreateVersion7());

    public static ReportExecutionId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Report execution id must not be an empty GUID.");
        }

        return new ReportExecutionId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>The stored file one completed execution produced.</summary>
public readonly record struct ReportArtifactId
{
    private ReportArtifactId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReportArtifactId New() => new(Guid.CreateVersion7());

    public static ReportArtifactId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Report artifact id must not be an empty GUID.");
        }

        return new ReportArtifactId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>A recurring report.</summary>
public readonly record struct ReportScheduleId
{
    private ReportScheduleId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReportScheduleId New() => new(Guid.CreateVersion7());

    public static ReportScheduleId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Report schedule id must not be an empty GUID.");
        }

        return new ReportScheduleId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
