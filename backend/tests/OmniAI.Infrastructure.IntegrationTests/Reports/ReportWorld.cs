using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Reports;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reports;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// A workspace with real members and real activity, seeded with leak canaries.
/// </summary>
/// <remarks>
/// Every piece of content a report must never carry — message text, agent
/// instructions, knowledge text, a raw WhatsApp number, a public chat key — is
/// written with a unique marker, so a privacy test can search a generated file for
/// the marker rather than hope to recognise a leak by eye.
/// </remarks>
internal sealed class ReportWorld(PostgresTestDatabase database)
{
    public const string MessageCanary = "CANARY-MESSAGE-7f3a";
    public const string InstructionsCanary = "CANARY-INSTRUCTIONS-91bd";
    public const string KnowledgeCanary = "CANARY-KNOWLEDGE-55e0";
    public const string RawNumberCanary = "15550009999";
    public const string PublicKeyCanary = "CANARYPK";

    public static readonly DateTimeOffset Day = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    public static readonly ReportPeriod September =
        ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

    private readonly PostgresTestDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public TenantId Tenant { get; private set; }

    public UserId Owner { get; private set; }

    public OmniAiDbContext Context() => _database.CreateContext();

    public static async Task<ReportWorld> CreateAsync(PostgresTestDatabase database, string name = "Acme Trading")
    {
        ReportWorld world = new(database);

        world.Owner = await Persistence.TestIdentity.SeedUserAsync(database);

        NewWorkspace workspace = NewWorkspace.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]),
            world.Owner,
            SeededPlans.Legacy,
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(workspace.Tenant);
        context.TenantMemberships.Add(workspace.Membership);
        context.TenantRoles.Add(workspace.OwnerRole);
        context.TenantRoleAssignments.Add(workspace.OwnerAssignment);
        context.Entry(workspace.OwnerRole).Property<string>("NormalisedName").CurrentValue =
            workspace.OwnerRole.Name.Normalised;

        await context.SaveChangesAsync();

        world.Tenant = workspace.Tenant.Id;

        return world;
    }

    /// <summary>A member holding exactly these permissions, through one custom role.</summary>
    public async Task<UserId> MemberAsync(params PermissionCode[] permissions)
    {
        UserId user = await Persistence.TestIdentity.SeedUserAsync(_database);

        TenantRole role = TenantRole.CreateCustom(
            Tenant,
            TenantRoleName.Create($"Role {Guid.NewGuid():N}"[..20]),
            permissions,
            DateTimeOffset.UtcNow);

        await using (OmniAiDbContext context = Context())
        {
            context.TenantMemberships.Add(TenantMembership.Create(user, Tenant, DateTimeOffset.UtcNow));
            context.TenantRoles.Add(role);
            context.Entry(role).Property<string>("NormalisedName").CurrentValue = role.Name.Normalised;

            await context.SaveChangesAsync();
        }

        await using OmniAiDbContext assigning = Context();

        assigning.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(user, role, DateTimeOffset.UtcNow));

        await assigning.SaveChangesAsync();

        return user;
    }

    public async Task RevokeAsync(UserId user)
    {
        await using OmniAiDbContext context = Context();

        await context.TenantRoleAssignments
            .Where(assignment => assignment.TenantId == Tenant && assignment.UserId == user)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// A web chat and a WhatsApp conversation, messages, a delivery, knowledge and
    /// two provider calls in two currencies — all of it carrying canaries.
    /// </summary>
    public async Task SeedActivityAsync(string customerName = "=HYPERLINK(\"https://evil.example\")", string agentName = "Support")
    {
        AiAgent agent = AiAgent.Create(
            Tenant,
            AiAgentName.Create(agentName),
            AgentInstructions.Create($"Always say {InstructionsCanary}."),
            Day);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(Tenant, agent.Id, WebChatPublicKey.Create(Key()), Day);

        Customer webCustomer = Customer.Create(Tenant, CustomerDisplayName.Create(customerName), Day);
        Customer phoneCustomer = Customer.Create(Tenant, null, Day);

        Conversation web = Conversation.Start(Tenant, webCustomer.Id, ChannelKind.WebChat, Day);
        web.AddTextMessage(MessageSender.Customer, $"Hello {MessageCanary}", Day);
        web.AddTextMessage(MessageSender.AiAgent, $"Reply {MessageCanary}", Day.AddMinutes(1));
        web.AddTextMessage(MessageSender.HumanAgent, $"Human {MessageCanary}", Day.AddMinutes(2));

        WebChatSession session = WebChatSession.Open(
            web.Id, Tenant, endpoint.Id, agent.Id, WebChatAccessTokenHash.From(Bytes(WebChatAccessTokenHash.Length)), Day, Day.AddHours(4));

        WhatsAppConnection connection = WhatsAppConnection.Create(
            Tenant, agent.Id, WabaId.Create(Digits()), PhoneNumberId.Create(Digits()), "+15550000000", Day);

        Conversation phone = Conversation.Start(Tenant, phoneCustomer.Id, ChannelKind.WhatsApp, Day.AddDays(1));
        Message outbound = phone.AddTextMessage(MessageSender.AiAgent, $"Answer {MessageCanary}", Day.AddDays(1).AddMinutes(1));
        phone.AddTextMessage(MessageSender.Customer, $"Question {MessageCanary}", Day.AddDays(1));

        WhatsAppContact contact = WhatsAppContact.Start(
            connection.Id, Tenant, WhatsAppCustomerId.Create(RawNumberCanary), phone.Id, Day.AddDays(1));

        WhatsAppDelivery delivery = WhatsAppDelivery.Queue(
            connection.Id, Tenant, phone.Id, outbound.Id, WhatsAppCustomerId.Create(RawNumberCanary), $"Answer {MessageCanary}", Day.AddDays(1));

        KnowledgeDocument document = KnowledgeDocument.Create(
            Tenant, KnowledgeTitle.Create("Policy"), KnowledgeContent.Create($"Returns: {KnowledgeCanary}"), Day);

        await using (OmniAiDbContext context = Context())
        {
            context.AiAgents.Add(agent);
            context.WebChatEndpoints.Add(endpoint);
            context.Customers.AddRange(webCustomer, phoneCustomer);
            context.Conversations.AddRange(web, phone);
            context.WebChatSessions.Add(session);
            context.WhatsAppConnections.Add(connection);
            context.WhatsAppContacts.Add(contact);
            context.WhatsAppDeliveries.Add(delivery);
            context.KnowledgeDocuments.Add(document);
            OmniAI.Infrastructure.Persistence.Repositories.KnowledgeDocumentRepository.StampFingerprint(
                context.Entry(document), document);

            await context.SaveChangesAsync();
        }

        await UsageAsync("model-usd", "USD", 1_000);
        await UsageAsync("model-eur", "EUR", 2_000);
    }

    public async Task UsageAsync(string model, string currency, long inputTokens)
    {
        AiProviderUsage operation = AiProviderUsage.Begin(
            Tenant, AiProvider.OpenAi, AiOperationType.TextGeneration, AiUsagePurpose.ConversationGeneration, ModelName.Create(model), Day);

        operation.Succeed(
            ProviderTokenUsage.Create(inputTokens: inputTokens, cachedInputTokens: null, outputTokens: 100, reasoningTokens: null),
            null,
            AiModelRates.Create(
                AiProvider.OpenAi,
                ModelName.Create(model),
                AiOperationType.TextGeneration,
                CurrencyCode.Create(currency),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                2.00m,
                cachedInputPerMillion: null,
                outputPerMillion: 8.00m),
            Day.AddSeconds(2));

        await using OmniAiDbContext context = Context();

        context.AiProviderUsages.Add(operation);

        await context.SaveChangesAsync();
    }

    public async Task<ReportExecution> PendingAsync(
        ReportType type = ReportType.ChannelUsage,
        ReportFormat format = ReportFormat.Csv,
        UserId? requester = null,
        DateTimeOffset? at = null,
        UserLanguage language = UserLanguage.English)
    {
        ReportExecution execution = ReportExecution.Request(
            Tenant, type, format, September, requester ?? Owner, Guid.NewGuid(), at ?? DateTimeOffset.UtcNow, language);

        await using OmniAiDbContext context = Context();

        await new ReportExecutionStore(context).AddOrGetAsync(execution);

        return execution;
    }

    private static string Key() => PublicKeyCanary + $"{Guid.NewGuid():N}"[..14];

    private static string Digits() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Random.Shared.NextInt64(100000000000, 999999999999)}");

    private static byte[] Bytes(int length)
    {
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);

        return bytes;
    }
}
