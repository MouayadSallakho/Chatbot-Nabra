using OmniAI.Api.Auth;
using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Reports;

/// <summary>
/// Report generation, generated reports, downloads and schedules.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Asking returns 202, not a file.</strong> A request is recorded and
/// answered with the execution it created; a background worker writes the file.
/// No request thread ever renders a spreadsheet.
/// </para>
/// <para>
/// <strong>Downloads come from here and nowhere else.</strong> There is no static
/// path, no pre-signed URL and no storage key in any response. Each download
/// re-checks membership, <c>reports.view</c> and the report's own data permission,
/// then streams the bytes through this process.
/// </para>
/// <para>
/// <strong>The file name is the server's.</strong> A fixed slug and two ISO dates;
/// nothing a user typed reaches <c>Content-Disposition</c>.
/// </para>
/// </remarks>
internal static class ReportEndpoints
{
    private const string GetExecutionRouteName = "GetReportExecution";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder reports = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/reports")
            .WithTags("Reports")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // Phase 37.6: the response types are declared so the generated document
        // describes the catalogue's codes, parameters and zone, and each
        // execution's timeZone.
        reports.MapGet("/catalog", CatalogAsync)
            .WithName("GetReportCatalog")
            .WithSummary("The reports this member may generate, by stable type code, with their parameters by code and the workspace time zone.")
            .Produces<ReportCatalogResponse>();
        reports.MapPost("/", RequestAsync)
            .WithName("RequestReport")
            .WithSummary("Asks for a report; its period is local days of the workspace time zone, kept on the execution. Optional language (en|ar) words the file; absent: the requester's own language, else en (400 language.unsupported otherwise).")
            .Produces<ReportExecutionResponse>(StatusCodes.Status202Accepted);
        reports.MapGet("/executions", ListAsync)
            .WithName("ListReportExecutions")
            .Produces<ReportExecutionPageResponse>();
        reports.MapGet("/executions/{executionId:guid}", GetAsync)
            .WithName(GetExecutionRouteName)
            .Produces<ReportExecutionResponse>();
        reports.MapGet("/executions/{executionId:guid}/download", DownloadAsync).WithName("DownloadReport");

        // Phase 37.7: the schedule routes declare their response types too, so the
        // generated document describes each schedule's language.
        reports.MapGet("/schedules", ListSchedulesAsync)
            .WithName("ListReportSchedules")
            .Produces<ReportScheduleListResponse>();
        reports.MapPost("/schedules", CreateScheduleAsync)
            .WithName("CreateReportSchedule")
            .WithSummary("Creates a schedule. Optional language (en|ar) words every occurrence; absent: the creator's own language, else en (400 language.unsupported otherwise).")
            .Produces<ReportScheduleResponse>(StatusCodes.Status201Created);
        reports.MapGet("/schedules/{scheduleId:guid}", GetScheduleAsync)
            .WithName("GetReportSchedule")
            .Produces<ReportScheduleResponse>();
        reports.MapPut("/schedules/{scheduleId:guid}", UpdateScheduleAsync)
            .WithName("UpdateReportSchedule")
            .WithSummary("Replaces a schedule's editable fields (If-Match). Optional language (en|ar) changes the language of its reports; absent keeps it.")
            .Produces<ReportScheduleResponse>();
        reports.MapPut("/schedules/{scheduleId:guid}/enabled", SetScheduleEnabledAsync)
            .WithName("SetReportScheduleEnabled")
            .Produces<ReportScheduleResponse>();

        return endpoints;
    }

    private static async Task<IResult> CatalogAsync(
        Guid tenantId,
        ReportHandlers handlers,
        CancellationToken cancellationToken)
    {
        ReportCatalogView? catalog = await handlers.CatalogAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return catalog is null ? NoSuchWorkspace() : Results.Ok(ReportCatalogResponse.From(catalog));
    }

    private static async Task<IResult> RequestAsync(
        Guid tenantId,
        RequestReportRequest? request,
        ReportHandlers handlers,
        CancellationToken cancellationToken)
    {
        // Phase 37.7: checked before membership, so the refusal says nothing about
        // the workspace.
        if (!AccountProblems.TryReadLanguage(request?.Language, out UserLanguage? language, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            (ReportExecutionView Execution, bool Created)? result = await handlers
                .RequestAsync(
                    new RequestReportCommand(
                        tenantId,
                        request?.ReportType,
                        request?.Format,
                        request?.Range,
                        request?.From,
                        request?.To,
                        request?.ClientRequestId,
                        language),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result is not { } accepted)
            {
                return NoSuchWorkspace();
            }

            // 202 whether it was just created or already existed: either way the
            // report is on its way, and the body says where it has got to.
            return Results.AcceptedAtRoute(
                GetExecutionRouteName,
                new { tenantId, executionId = accepted.Execution.Id },
                ReportExecutionResponse.From(accepted.Execution));
        }
        catch (ReportRequestConflictException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "That request id was already used",
                detail: failure.Message);
        }
        catch (ReportStorageUnavailableException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Reports are not available on this deployment",
                detail: "Report storage is not configured, so no report can be generated here.");
        }
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        ReportHandlers handlers,
        CancellationToken cancellationToken)
    {
        ReportExecutionPage? result = await handlers
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoSuchWorkspace() : Results.Ok(ReportExecutionPageResponse.From(result));
    }

    private static async Task<IResult> GetAsync(
        Guid tenantId,
        Guid executionId,
        ReportHandlers handlers,
        CancellationToken cancellationToken)
    {
        ReportExecutionView? execution = await handlers
            .GetAsync(tenantId, executionId, cancellationToken)
            .ConfigureAwait(false);

        return execution is null ? NoSuchReport() : Results.Ok(ReportExecutionResponse.From(execution));
    }

    private static async Task<IResult> DownloadAsync(
        Guid tenantId,
        Guid executionId,
        HttpContext http,
        ReportHandlers handlers,
        CancellationToken cancellationToken)
    {
        try
        {
            ReportDownload? download = await handlers
                .DownloadAsync(tenantId, executionId, cancellationToken)
                .ConfigureAwait(false);

            if (download is null)
            {
                return NoSuchReport();
            }

            // Business data: never cached by a browser or a proxy, never sniffed
            // into something the browser would render.
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";

            return Results.File(download.Content, download.ContentType, download.FileName);
        }
        catch (ReportNotDownloadableException failure) when (failure.Status == ReportExecutionStatus.Expired)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "This report is no longer available",
                detail: failure.Message);
        }
        catch (ReportNotDownloadableException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This report has no file to download",
                detail: failure.Message);
        }
    }

    private static async Task<IResult> ListSchedulesAsync(
        Guid tenantId,
        ReportScheduleHandlers handlers,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportScheduleView>? schedules = await handlers
            .ListAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return schedules is null
            ? NoSuchWorkspace()
            : Results.Ok(new ReportScheduleListResponse([.. schedules.Select(ReportScheduleResponse.From)]));
    }

    private static async Task<IResult> GetScheduleAsync(
        Guid tenantId,
        Guid scheduleId,
        HttpContext http,
        ReportScheduleHandlers handlers,
        CancellationToken cancellationToken) =>
        Respond(http, await handlers.GetAsync(tenantId, scheduleId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateScheduleAsync(
        Guid tenantId,
        SaveReportScheduleRequest? request,
        HttpContext http,
        ReportScheduleHandlers handlers,
        CancellationToken cancellationToken)
    {
        if (!AccountProblems.TryReadLanguage(request?.Language, out UserLanguage? language, out IResult? unsupported))
        {
            return unsupported!;
        }

        try
        {
            Versioned<ReportScheduleView>? created = await handlers
                .CreateAsync(Command(tenantId, null, request, default, language), cancellationToken)
                .ConfigureAwait(false);

            if (created is null)
            {
                return NoSuchWorkspace();
            }

            http.Response.Headers.ETag = EntityTags.Format(created.Version);

            return Results.Created(
                $"/api/tenants/{tenantId}/reports/schedules/{created.Value.Id}",
                ReportScheduleResponse.From(created.Value));
        }
        catch (ReportScheduleLimitException failure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Schedule limit reached",
                detail: failure.Message);
        }
    }

    private static async Task<IResult> UpdateScheduleAsync(
        Guid tenantId,
        Guid scheduleId,
        SaveReportScheduleRequest? request,
        HttpContext http,
        ReportScheduleHandlers handlers,
        CancellationToken cancellationToken)
    {
        if (!AccountProblems.TryReadLanguage(request?.Language, out UserLanguage? language, out IResult? unsupported))
        {
            return unsupported!;
        }

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult refusal)
        {
            return refusal;
        }

        try
        {
            return Respond(
                http,
                await handlers
                    .UpdateAsync(Command(tenantId, scheduleId, request, expected, language), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (ReportScheduleVersionMismatchException failure)
        {
            return Stale(failure.Message);
        }
    }

    private static async Task<IResult> SetScheduleEnabledAsync(
        Guid tenantId,
        Guid scheduleId,
        SetReportScheduleEnabledRequest? request,
        HttpContext http,
        ReportScheduleHandlers handlers,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult refusal)
        {
            return refusal;
        }

        if (request?.Enabled is not bool enabled)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Enabled is required",
                detail: "Say whether the schedule should be on or off.");
        }

        try
        {
            return Respond(
                http,
                await handlers
                    .SetEnabledAsync(
                        new SetReportScheduleEnabledCommand(tenantId, scheduleId, enabled, expected),
                        cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (ReportScheduleVersionMismatchException failure)
        {
            return Stale(failure.Message);
        }
    }

    private static SaveReportScheduleCommand Command(
        Guid tenantId,
        Guid? scheduleId,
        SaveReportScheduleRequest? request,
        ConcurrencyToken expected,
        UserLanguage? language) =>
        new(
            tenantId,
            scheduleId,
            request?.Name,
            request?.ReportType,
            request?.Format,
            request?.Frequency,
            request?.Enabled,
            expected,
            language);

    private static IResult Respond(HttpContext http, Versioned<ReportScheduleView>? schedule)
    {
        if (schedule is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such schedule",
                detail: "This schedule does not exist in this workspace.");
        }

        http.Response.Headers.ETag = EntityTags.Format(schedule.Version);

        return Results.Ok(ReportScheduleResponse.From(schedule.Value));
    }

    private static IResult Stale(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status412PreconditionFailed,
            title: "This schedule changed",
            detail: detail);

    private static IResult NoSuchWorkspace() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No such workspace",
            detail: "This workspace does not exist, or you are not a member of it.");

    private static IResult NoSuchReport() =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "No such report",
            detail: "This report does not exist in this workspace.");
}
