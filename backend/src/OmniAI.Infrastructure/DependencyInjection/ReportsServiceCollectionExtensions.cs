using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Reports;
using OmniAI.Infrastructure.Reports;
using OmniAI.Infrastructure.Reports.Rendering;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers reports: the stores, the renderers, the artifact store and the workers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No storage configured is a supported state.</strong> The application
/// starts, the catalogue says reports cannot be generated here, and a request is
/// refused with a clear reason. Nothing falls back to the working directory.
/// </para>
/// <para>
/// <strong>Storage configured badly is not.</strong> A relative path, a path
/// inside the application or inside a source repository stops startup with a
/// message naming the setting: somebody tried to configure it, and quietly
/// ignoring them would be worse than refusing.
/// </para>
/// </remarks>
public static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddOmniAiReports(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contentRootPath);

        ReportOptions options = new();
        configuration.GetSection(ReportOptions.Section).Bind(options);
        options.Validate();
        services.AddSingleton(options);

        ReportStorageOptions storage = new();
        configuration.GetSection(ReportStorageOptions.Section).Bind(storage);

        string? root = string.IsNullOrWhiteSpace(storage.RootPath)
            ? null
            : FileSystemReportArtifactStore.ValidateRoot(storage.RootPath.Trim(), contentRootPath);

        // A singleton: it holds two directory names and no request state.
        services.AddSingleton<IReportArtifactStore>(new FileSystemReportArtifactStore(root));

        services.AddScoped<IReportExecutionStore, ReportExecutionStore>();
        services.AddScoped<IReportScheduleStore, ReportScheduleStore>();
        services.AddScoped<ICustomerActivityReader, CustomerActivityReader>();

        // Renderers hold nothing between documents.
        services.AddSingleton<IReportRenderer, CsvReportRenderer>();
        services.AddSingleton<IReportRenderer, XlsxReportRenderer>();
        services.AddSingleton<IReportRenderer, PdfReportRenderer>();

        services.AddScoped<ReportAuthority>();
        services.AddScoped<ReportDocumentBuilder>();
        services.AddScoped<ReportHandlers>();
        services.AddScoped<ReportScheduleHandlers>();
        services.AddScoped<ReportGenerator>();
        services.AddScoped<ReportScheduleRunner>();
        services.AddScoped<ReportRetention>();

        ReportWorkerOptions workers = new();
        configuration.GetSection(ReportWorkerOptions.SectionName).Bind(workers);
        workers.Validate();

        services.Configure<ReportWorkerOptions>(configuration.GetSection(ReportWorkerOptions.SectionName));

        services.AddHostedService<ReportGenerationWorker>();
        services.AddHostedService<ReportScheduleWorker>();
        services.AddHostedService<ReportRetentionWorker>();

        return services;
    }
}
