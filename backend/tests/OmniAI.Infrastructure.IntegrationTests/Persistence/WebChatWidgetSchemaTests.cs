using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What PostgreSQL enforces about the embeddable widget.
/// </summary>
/// <remarks>
/// <strong>The allowlist is a security control, so its shape is asserted against
/// the database rather than trusted from the mapping.</strong> A duplicate row
/// would make removing a website an act that has to be performed twice; a
/// tenant-blind foreign key would let one workspace approve a website for
/// another's endpoint.
/// </remarks>
public sealed class WebChatWidgetSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_widget_table_has_a_physical_xmin_column()
    {
        // The ETag is built from PostgreSQL's own per-row transaction id. A real
        // column of that name would shadow the system one and silently disable
        // every precondition built on it -- and the scaffolder emits one unless it
        // is removed by hand.
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND column_name = 'xmin'");

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task The_endpoint_carries_its_appearance_and_nothing_executable()
    {
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || ':' || data_type, ', ' ORDER BY column_name) "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'web_chat_endpoints' "
            + "AND column_name LIKE 'widget%'");

        // Four values, all short strings. No CSS, no HTML, no script, no URL, and
        // nothing a tenant could put a font or an image in: what runs on a
        // visitor's machine is not something a settings screen may decide.
        Assert.Equal(
            "widget_accent_color:character varying, "
            + "widget_greeting:character varying, "
            + "widget_greeting_ar:character varying, "
            + "widget_greeting_en:character varying, "
            + "widget_launcher_position:character varying, "
            + "widget_theme:character varying",
            columns as string);
    }

    [Fact]
    public async Task The_appearance_columns_are_required_except_the_greeting()
    {
        object? nullable = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || '=' || is_nullable, ', ' ORDER BY column_name) "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'web_chat_endpoints' "
            + "AND column_name LIKE 'widget%'");

        // A greeting is genuinely optional, and so is each language's own
        // (Phase 39.1). The other three always have a value,
        // because a widget with no colour or no corner could not be rendered at
        // all -- and every row that existed before Phase 18 was backfilled with
        // the platform defaults rather than an empty string.
        Assert.Equal(
            "widget_accent_color=NO, widget_greeting=YES, widget_greeting_ar=YES, "
            + "widget_greeting_en=YES, widget_launcher_position=NO, widget_theme=NO",
            nullable as string);
    }

    [Fact]
    public async Task One_website_can_be_approved_only_once_for_one_widget()
    {
        object? key = await database.ExecuteScalarAsync(
            "SELECT string_agg(a.attname, ', ' ORDER BY a.attname) "
            + "FROM pg_index i "
            + "JOIN pg_class t ON t.oid = i.indrelid "
            + "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(i.indkey) "
            + "WHERE t.relname = 'web_chat_allowed_origins' AND i.indisprimary");

        // Duplicates are impossible, which is what makes revoking a website a
        // single deterministic act rather than a loop.
        Assert.Equal("origin, web_chat_endpoint_id", key as string);
    }

    [Fact]
    public async Task An_approved_website_belongs_to_its_endpoint_and_its_workspace()
    {
        object? foreignKey = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_web_chat_allowed_origins_endpoint'");

        string definition = Assert.IsType<string>(foreignKey);

        // Composite, onto (id, tenant_id). One workspace cannot approve a website
        // for another workspace's endpoint, because the single tenant_id on the
        // row would have to satisfy a key that disagrees.
        Assert.Contains("web_chat_endpoint_id, tenant_id", definition, StringComparison.Ordinal);
        Assert.Contains("(id, tenant_id)", definition, StringComparison.Ordinal);
        Assert.Contains("ON DELETE CASCADE", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_agrees_with_itself_about_where_it_came_from()
    {
        object? check = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'ck_web_chat_sessions_embedded_origin'");

        string definition = Assert.IsType<string>(check);

        // An embedded session that did not know its website could not be revoked
        // with that website; a hosted session claiming one would be claiming
        // something that never happened.
        Assert.Contains("Embedded", definition, StringComparison.Ordinal);
        Assert.Contains("embedded_origin", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_widget_survives_a_round_trip_through_the_database()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId workspace = await SeedWorkspaceAsync(context);
        AiAgentId agent = await SeedAgentAsync(context, workspace);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            workspace,
            agent,
            WebChatPublicKey.Create(Guid.NewGuid().ToString("N")[..WebChatPublicKey.Length]),
            Noon);

        endpoint.Configure(
            WebChatWidgetSettings.Create(
                WebChatGreeting.CreateOrNone("Hi — ask us anything."),
                WebChatAccentColor.Create("#123456"),
                WebChatLauncherPosition.Left,
                WebChatTheme.Dark),
            [
                WebChatOrigin.Create("https://www.example.com"),
                WebChatOrigin.Create("https://shop.example.com:8443"),
            ],
            Noon);

        context.WebChatEndpoints.Add(endpoint);
        await context.SaveChangesAsync();

        await using OmniAiDbContext fresh = database.CreateContext();

        WebChatEndpoint? read = await fresh.WebChatEndpoints
            .SingleOrDefaultAsync(candidate => candidate.Id == endpoint.Id);

        Assert.NotNull(read);
        Assert.Equal("Hi — ask us anything.", read.Widget.Greeting?.Value);
        Assert.Equal("#123456", read.Widget.Accent.Value);
        Assert.Equal(WebChatLauncherPosition.Left, read.Widget.Position);
        Assert.Equal(WebChatTheme.Dark, read.Widget.Theme);

        Assert.True(read.Allows(WebChatOrigin.Create("https://www.example.com")));
        Assert.True(read.Allows(WebChatOrigin.Create("https://shop.example.com:8443")));
        Assert.False(read.Allows(WebChatOrigin.Create("https://shop.example.com")));
    }

    [Fact]
    public async Task Removing_a_website_removes_its_row()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId workspace = await SeedWorkspaceAsync(context);
        AiAgentId agent = await SeedAgentAsync(context, workspace);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            workspace,
            agent,
            WebChatPublicKey.Create(Guid.NewGuid().ToString("N")[..WebChatPublicKey.Length]),
            Noon);

        endpoint.Configure(
            WebChatWidgetSettings.Default,
            [WebChatOrigin.Create("https://gone.example.com")],
            Noon);

        context.WebChatEndpoints.Add(endpoint);
        await context.SaveChangesAsync();

        endpoint.Configure(WebChatWidgetSettings.Default, [], Noon.AddMinutes(1));

        await context.SaveChangesAsync();

        await using OmniAiDbContext fresh = database.CreateContext();

        WebChatEndpoint? read = await fresh.WebChatEndpoints
            .SingleOrDefaultAsync(candidate => candidate.Id == endpoint.Id);

        Assert.NotNull(read);

        // Not merely unreachable: the row is gone. A revoked website that stayed
        // in the table would be one a later bug could put back.
        Assert.Empty(read.AllowedOrigins);
    }

    private static async Task<TenantId> SeedWorkspaceAsync(OmniAiDbContext context)
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create($"Widget {Guid.NewGuid():N}"),
            TenantSlug.Create($"widget-{Guid.NewGuid():N}"[..24]));

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return tenant.Id;
    }

    private static async Task<AiAgentId> SeedAgentAsync(OmniAiDbContext context, TenantId workspace)
    {
        AiAgent agent = AiAgent.Create(
            workspace,
            AiAgentName.Create("Support"),
            AgentInstructions.Create("Answer politely and briefly."),
            Noon);

        agent.Activate(Noon);

        context.AiAgents.Add(agent);
        await context.SaveChangesAsync();

        return agent.Id;
    }
}
