using OmniAI.Application.Analytics;
using OmniAI.Application.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Analytics;

/// <summary>
/// The analytics handler: who may read, and what it costs.
/// </summary>
/// <remarks>
/// The counting itself is proved against a real PostgreSQL, where it belongs.
/// What is proved here is everything around it — the permission, the window, and
/// the fact that opening a dashboard reaches no AI provider.
/// </remarks>
public sealed class WorkspaceAnalyticsHandlerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Workspace = Guid.NewGuid();

    [Fact]
    public async Task An_owner_reads_the_dashboard()
    {
        RecordingAnalyticsReader reader = new();

        WorkspaceAnalytics? analytics = await HandlerFor(reader, Owner())
            .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays));

        Assert.NotNull(analytics);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task A_member_holding_analytics_view_reads_the_dashboard()
    {
        RecordingAnalyticsReader reader = new();

        TenantAuthorizer authorizer = TestAuthority.With(
            UserId.New(),
            Workspace,
            PlatformPermissions.AnalyticsView);

        WorkspaceAnalytics? analytics = await HandlerFor(reader, authorizer)
            .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays));

        Assert.NotNull(analytics);
    }

    [Fact]
    public async Task A_member_without_analytics_view_is_refused_before_anything_is_read()
    {
        RecordingAnalyticsReader reader = new();

        // A member, with a real role that grants something else entirely.
        TenantAuthorizer authorizer = TestAuthority.With(
            UserId.New(),
            Workspace,
            PlatformPermissions.ConversationsView);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => HandlerFor(reader, authorizer)
                .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays)));

        // Refused before the database was touched: a refusal that still ran eight
        // aggregate queries would be a way to make a deployment work for somebody
        // who may not see the answer.
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task Somebody_who_is_not_a_member_gets_absence_rather_than_a_refusal()
    {
        RecordingAnalyticsReader reader = new();

        WorkspaceAnalytics? analytics = await HandlerFor(reader, TestAuthority.Outsider(UserId.New()))
            .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays));

        // Null becomes a 404 at the edge. Somebody outside the workspace learns
        // nothing about whether it exists.
        Assert.Null(analytics);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task The_workspace_asked_for_is_the_workspace_read()
    {
        RecordingAnalyticsReader reader = new();

        await HandlerFor(reader, Owner())
            .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays));

        Assert.Equal(TenantId.From(Workspace), reader.LastTenant);
    }

    [Theory]
    [InlineData(AnalyticsRange.SevenDays, 7)]
    [InlineData(AnalyticsRange.ThirtyDays, 30)]
    [InlineData(AnalyticsRange.NinetyDays, 90)]
    public async Task The_window_comes_from_the_clock_and_the_range(
        AnalyticsRange range,
        int days)
    {
        RecordingAnalyticsReader reader = new();

        await HandlerFor(reader, Owner())
            .HandleAsync(new GetWorkspaceAnalyticsQuery(Workspace, range));

        Assert.NotNull(reader.LastWindow);
        Assert.Equal(Noon, reader.LastWindow.To);
        Assert.Equal(days, reader.LastWindow.Days().Count);
        Assert.Equal(Noon.AddDays(-(days - 1)).Date, reader.LastWindow.From.UtcDateTime.Date);
    }

    /// <remarks>
    /// The claim this whole feature rests on: a dashboard is free. The handler
    /// takes no generator and no embedding provider at all, which is the strongest
    /// form the guarantee can take — there is nothing to call.
    /// </remarks>
    [Fact]
    public void Reading_analytics_cannot_reach_an_ai_provider()
    {
        Type[] dependencies =
        [
            .. typeof(GetWorkspaceAnalyticsHandler)
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
        ];

        Assert.DoesNotContain(
            dependencies,
            type => type.Name.Contains("Generator", StringComparison.Ordinal)
                || type.Name.Contains("Embedding", StringComparison.Ordinal)
                || type.Name.Contains("AiText", StringComparison.Ordinal));
    }

    private static TenantAuthorizer Owner() =>
        TestAuthority.Owner(UserId.New(), Workspace);

    private static GetWorkspaceAnalyticsHandler HandlerFor(
        IWorkspaceAnalyticsReader reader,
        TenantAuthorizer authorizer) =>
        new(reader, authorizer, new FixedTimeProvider(Noon));
}

/// <summary>
/// A reader that answers with nothing and remembers being asked.
/// </summary>
/// <remarks>
/// Zeroes rather than invented activity. These tests are about who may ask and
/// with what window; fabricating numbers here would make the assertions about
/// the double rather than about the handler.
/// </remarks>
internal sealed class RecordingAnalyticsReader : IWorkspaceAnalyticsReader
{
    public int Reads { get; private set; }

    public TenantId? LastTenant { get; private set; }

    public AnalyticsWindow? LastWindow { get; private set; }

    public Task<WorkspaceAnalytics> ReadAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        Reads++;
        LastTenant = tenantId;
        LastWindow = window;

        return Task.FromResult(new WorkspaceAnalytics(
            new AnalyticsPeriod(
                window.Label,
                window.From,
                window.To,
                window.Range.Days(),
                AnalyticsWindow.TimeZone),
            new AnalyticsSummary(0, 0, 0, 0, 0),
            new AnalyticsSnapshot(0, 0, 0),
            [],
            [],
            [],
            new AnalyticsAiOperations(0, 0, 0, 0),
            WhatsAppDelivery: null,
            new AnalyticsAiUsage(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
            []));
    }
}
