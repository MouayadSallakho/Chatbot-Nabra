using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Plans;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Plans;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Plans;

/// <summary>
/// A workspace on a plan, with real rows behind it.
/// </summary>
/// <remarks>
/// <strong>Everything here is written through the aggregates into real
/// PostgreSQL.</strong> Entitlements are counts over operational tables and a
/// lock held across a transaction; neither can be proved against in-memory
/// objects, because what is being tested is precisely what the database does when
/// two callers arrive at once.
/// </remarks>
internal sealed class PlanWorld(PostgresTestDatabase database)
{
    private readonly PostgresTestDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    public Tenant Tenant { get; private set; } = null!;

    public Plan Plan { get; private set; } = null!;

    /// <summary>Builds a workspace on a plan with the given caps.</summary>
    /// <remarks>
    /// Every cap defaults to unlimited, so a test names only the one it is about.
    /// </remarks>
    public async Task<PlanWorld> StartAsync(
        int? members = null,
        int? agents = null,
        int? documents = null,
        int? webChat = null,
        int? whatsApp = null,
        int? monthlyOperations = null)
    {
        Plan = Plan.Create(
            PlanCode.Create($"p{Guid.NewGuid():N}"[..20]),
            PlanDisplayName.Create("Test plan"),
            PlanLimit.From(members),
            PlanLimit.From(agents),
            PlanLimit.From(documents),
            PlanLimit.From(webChat),
            PlanLimit.From(whatsApp),
            PlanLimit.From(monthlyOperations),
            DateTimeOffset.UtcNow);

        Tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = _database.CreateContext();

        context.Plans.Add(Plan);
        context.Tenants.Add(Tenant);

        // Added before SaveChanges, so the fixture's own legacy-plan interceptor
        // leaves it alone: a test's plan is the one under test.
        context.WorkspacePlanAssignments.Add(
            WorkspacePlanAssignment.Create(Tenant.Id, Plan.Id, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();

        return this;
    }

    /// <summary>An entitlement service over its own unit of work.</summary>
    /// <remarks>
    /// A fresh context per call, because two competing writers need two. Sharing
    /// one would serialise them in the change tracker and prove nothing about the
    /// database.
    /// </remarks>
    public (IWorkspaceEntitlementService Service, OmniAiDbContext Context) Service(
        DateTimeOffset? now = null)
    {
        OmniAiDbContext context = _database.CreateContext();

        return (
            new WorkspaceEntitlementService(
                context,
                new FixedTimeProvider(now ?? DateTimeOffset.UtcNow)),
            context);
    }

    // ---------------------------------------------------------------- seeding

    public async Task AddAgentsAsync(int count)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            context.AiAgents.Add(AiAgent.Create(
                Tenant.Id,
                AiAgentName.Create($"Agent {index}"),
                AgentInstructions.Create("Be helpful."),
                DateTimeOffset.UtcNow));
        }

        await context.SaveChangesAsync();
    }

    public async Task AddDocumentsAsync(int count)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            KnowledgeDocument document = KnowledgeDocument.Create(
                Tenant.Id,
                KnowledgeTitle.Create($"Policy {index}"),
                KnowledgeContent.Create($"Goods may be returned within {index + 30} days."),
                DateTimeOffset.UtcNow);

            context.KnowledgeDocuments.Add(document);

            // The fingerprint is a shadow property the repository stamps. Seeding
            // straight through the context has to do the same, or the row is
            // refused by a NOT NULL that exists for a good reason.
            KnowledgeDocumentRepository.StampFingerprint(context.Entry(document), document);
        }

        await context.SaveChangesAsync();
    }

    public async Task AddWebChatAsync(int count)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            AiAgent agent = AiAgent.Create(
                Tenant.Id,
                AiAgentName.Create($"Chat agent {index}"),
                AgentInstructions.Create("Be helpful."),
                DateTimeOffset.UtcNow);

            context.AiAgents.Add(agent);

            context.WebChatEndpoints.Add(WebChatEndpoint.Create(
                Tenant.Id,
                agent.Id,
                WebChatPublicKey.Create($"{Guid.NewGuid():N}"[..22]),
                DateTimeOffset.UtcNow));
        }

        await context.SaveChangesAsync();
    }

    public async Task AddWhatsAppAsync(int count)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            AiAgent agent = AiAgent.Create(
                Tenant.Id,
                AiAgentName.Create($"WhatsApp agent {index}"),
                AgentInstructions.Create("Be helpful."),
                DateTimeOffset.UtcNow);

            context.AiAgents.Add(agent);

            context.WhatsAppConnections.Add(WhatsAppConnection.Create(
                Tenant.Id,
                agent.Id,
                WabaId.Create(Digits()),
                PhoneNumberId.Create(Digits()),
                "+15550000000",
                DateTimeOffset.UtcNow));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Adds members. Each gets a real account row behind it.</summary>
    /// <remarks>
    /// The membership's foreign key insists on a real user, which is the database
    /// keeping the same promise the application does. Inventing an id would work
    /// only until the constraint noticed.
    /// </remarks>
    public async Task AddMembersAsync(int count)
    {
        for (int index = 0; index < count; index++)
        {
            await JoinAsync(await NewAccountAsync());
        }
    }

    /// <summary>An account that can hold a membership.</summary>
    public async Task<Domain.Users.UserId> NewAccountAsync()
    {
        Guid id = Guid.CreateVersion7();

        await _database.ExecuteScalarAsync(
            $"""
            INSERT INTO users (
                id, user_name, normalized_user_name, email, normalized_email,
                email_confirmed, phone_number_confirmed, two_factor_enabled,
                lockout_enabled, access_failed_count, security_stamp, concurrency_stamp)
            VALUES (
                '{id}', 'u{id:N}@test.local', 'U{id:N}@TEST.LOCAL',
                'u{id:N}@test.local', 'U{id:N}@TEST.LOCAL',
                true, false, false, false, 0, '{Guid.NewGuid()}', '{Guid.NewGuid()}')
            RETURNING 1
            """);

        return Domain.Users.UserId.From(id);
    }

    /// <summary>Adds one membership directly, without taking a seat.</summary>
    public async Task JoinAsync(Domain.Users.UserId userId)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        context.TenantMemberships.Add(
            TenantMembership.Create(userId, Tenant.Id, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }

    /// <summary>Provider operations dispatched at a given instant.</summary>
    public async Task AddOperationsAsync(int count, DateTimeOffset at)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            context.AiProviderUsages.Add(Domain.Ai.AiProviderUsage.Begin(
                Tenant.Id,
                Domain.Ai.AiProvider.OpenAi,
                Domain.Ai.AiOperationType.TextGeneration,
                Domain.Ai.AiUsagePurpose.ConversationGeneration,
                Domain.Ai.ModelName.Create("test-model-x"),
                at));
        }

        await context.SaveChangesAsync();
    }

    public async Task<int> CountAgentsAsync()
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.AiAgents.CountAsync(agent => agent.TenantId == Tenant.Id);
    }

    public async Task<int> CountOperationsAsync()
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.AiProviderUsages.CountAsync(
            operation => operation.TenantId == Tenant.Id);
    }

    public async Task<int> CountMembersAsync()
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.TenantMemberships.CountAsync(
            membership => membership.TenantId == Tenant.Id);
    }

    private static string Digits() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Random.Shared.NextInt64(100000000000, 999999999999)}");
}
