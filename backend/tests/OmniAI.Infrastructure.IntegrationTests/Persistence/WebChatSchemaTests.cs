using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What PostgreSQL itself enforces about website chat and the conversation runtime.
/// </summary>
/// <remarks>
/// <strong>These are the rules that survive a bug in a handler.</strong> Every one
/// of them is a property the application also checks, asserted here against the
/// database so that a future refactor which drops the check in code fails a test
/// rather than a customer.
/// </remarks>
public sealed class WebChatSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task No_table_stores_a_raw_conversation_secret()
    {
        // The central privacy claim of this phase, asserted against the schema
        // rather than trusted from the code. The only credential-shaped column in
        // the whole database is a digest, and it is bytea.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(table_name || '.' || column_name || ':' || data_type, ', ' "
            + "ORDER BY table_name, column_name) FROM information_schema.columns "
            + "WHERE table_schema = 'public' "
            + "AND (column_name LIKE '%token%' OR column_name LIKE '%secret%')");

        // tenant_invitations.token_hash is Phase 14's, and is also a digest.
        Assert.Equal(
            "tenant_invitations.token_hash:bytea, web_chat_sessions.access_token_hash:bytea",
            columns as string);
    }

    [Fact]
    public async Task The_session_table_has_exactly_the_columns_it_needs()
    {
        // No visitor name, no IP address, no user agent, no email. An anonymous
        // conversation stays anonymous, and this is what would notice a field
        // being added quietly.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name, ', ' ORDER BY ordinal_position) "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'web_chat_sessions'");

        Assert.Equal(
            "conversation_id, tenant_id, web_chat_endpoint_id, ai_agent_id, "
            + "access_token_hash, created_at",
            columns as string);
    }

    [Fact]
    public async Task The_endpoint_table_has_no_physical_xmin_column()
    {
        // Its ETag is built from PostgreSQL's own per-row transaction id, which
        // every table already has. A column of that name would shadow the system
        // one and quietly break the concurrency check -- and the scaffolder does
        // emit one unless it is removed by hand.
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'web_chat_endpoints' "
            + "AND column_name = 'xmin'");

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task A_public_key_names_exactly_one_endpoint_across_the_deployment()
    {
        // What lets the public lookup take no tenant. Without it, two workspaces
        // could share a key and a visitor could reach either.
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' AND indexname = 'ux_web_chat_endpoints_public_key'");

        Assert.Contains("UNIQUE", unique as string ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workspace_publishes_at_most_one_link_per_agent()
    {
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' AND indexname = 'ux_web_chat_endpoints_tenant_agent'");

        Assert.Contains("UNIQUE", unique as string ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_conversations_cannot_share_a_credential()
    {
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' "
            + "AND indexname = 'ux_web_chat_sessions_access_token_hash'");

        Assert.Contains("UNIQUE", unique as string ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_client_message_id_opens_at_most_one_turn_per_conversation()
    {
        // The idempotency boundary. Two concurrent requests both pass any
        // in-process check; this index is the only thing that can settle the race,
        // and it is what stops a retry buying a second reply.
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' "
            + "AND indexname = 'ux_conversation_turns_client_message'");

        string definition = unique as string ?? string.Empty;

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("conversation_id", definition, StringComparison.Ordinal);
        Assert.Contains("client_message_id", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_customer_message_belongs_to_at_most_one_turn()
    {
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' "
            + "AND indexname = 'ux_conversation_turns_customer_message'");

        Assert.Contains("UNIQUE", unique as string ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_and_its_agent_must_belong_to_the_same_workspace()
    {
        // A composite foreign key onto (id, tenant_id), so a cross-workspace
        // pairing is not something to validate against -- it is a row the database
        // will not accept.
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_web_chat_endpoints_agent'");

        string constraint = definition as string ?? string.Empty;

        Assert.Contains("ai_agent_id", constraint, StringComparison.Ordinal);
        Assert.Contains("tenant_id", constraint, StringComparison.Ordinal);
        Assert.Contains("ai_agents", constraint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_and_its_endpoint_must_belong_to_the_same_workspace()
    {
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_web_chat_sessions_endpoint'");

        string constraint = definition as string ?? string.Empty;

        Assert.Contains("web_chat_endpoint_id", constraint, StringComparison.Ordinal);
        Assert.Contains("tenant_id", constraint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_and_its_agent_must_belong_to_the_same_workspace()
    {
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_web_chat_sessions_agent'");

        string constraint = definition as string ?? string.Empty;

        Assert.Contains("ai_agent_id", constraint, StringComparison.Ordinal);
        Assert.Contains("tenant_id", constraint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_belongs_to_exactly_one_conversation()
    {
        // The conversation is the primary key, which is what makes a second claim
        // on somebody else's conversation unrepresentable rather than refused.
        object? key = await database.ExecuteScalarAsync(
            "SELECT string_agg(a.attname, ', ' ORDER BY a.attnum) "
            + "FROM pg_constraint c "
            + "JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey) "
            + "WHERE c.conrelid = 'web_chat_sessions'::regclass AND c.contype = 'p'");

        Assert.Equal("conversation_id", key as string);
    }

    [Fact]
    public async Task A_turn_cannot_outlive_the_conversation_it_belongs_to()
    {
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_conversation_turns_conversation'");

        Assert.Contains(
            "ON DELETE CASCADE",
            definition as string ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_message_ingestion_sequence_is_still_the_only_ordering()
    {
        // Phase 15 adds nothing that could become a competing order. The runtime
        // reads and pages history by this and nothing else.
        object? index = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE schemaname = 'public' AND indexname = 'ix_messages_conversation_ingestion'");

        string definition = index as string ?? string.Empty;

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("ingestion_sequence", definition, StringComparison.Ordinal);
    }
}
