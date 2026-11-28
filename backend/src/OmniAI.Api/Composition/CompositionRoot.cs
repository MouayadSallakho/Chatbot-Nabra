using OmniAI.Api.ErrorHandling;
using OmniAI.Api.Security;
using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.PreviewAiAgent;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.Authorization.CreateTenantRole;
using OmniAI.Application.Authorization.GetMyTenantPermissions;
using OmniAI.Application.Authorization.GetPermissionCatalog;
using OmniAI.Application.Authorization.GetTenantRoleById;
using OmniAI.Application.Authorization.ListTenantRoles;
using OmniAI.Application.Authorization.RenameTenantRole;
using OmniAI.Application.Authorization.ReplaceTenantRolePermissions;
using OmniAI.Application.Authorization;
using OmniAI.Application.Invitations.AcceptTenantInvitation;
using OmniAI.Application.Invitations.CreateTenantInvitation;
using OmniAI.Application.Invitations.ListTenantInvitations;
using OmniAI.Application.Invitations.RevokeTenantInvitation;
using OmniAI.Application.Invitations;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Analytics;
using OmniAI.Application.Billing;
using OmniAI.Application.Plans;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Application.Knowledge.AgentKnowledge;
using OmniAI.Application.Knowledge.CreateKnowledgeDocument;
using OmniAI.Application.Knowledge.GetKnowledgeDocumentById;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.SearchAgentKnowledge;
using OmniAI.Application.Knowledge.SetKnowledgeDocumentStatus;
using OmniAI.Application.Knowledge.UpdateKnowledgeDocument;
using OmniAI.Application.Members.GetAssignableRoles;
using OmniAI.Application.Members.GetTenantMemberById;
using OmniAI.Application.Members.ListTenantMembers;
using OmniAI.Application.Members.ReplaceTenantMemberRoles;
using Microsoft.AspNetCore.SignalR;
using OmniAI.Api.Realtime;
using OmniAI.Application.Accounts;
using OmniAI.Infrastructure.Accounts;
using OmniAI.Application.Notifications;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Infrastructure.Conversations.Realtime;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Application.Conversations;
using OmniAI.Application.Tenants.ChangeTimeZone;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.WebChat.CreateWebChatEndpoint;
using OmniAI.Application.WebChat.GetWebChatEndpointById;
using OmniAI.Application.WebChat.Embedded;
using OmniAI.Application.WebChat.GetWebChatWidget;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.ListWebChatEndpoints;
using OmniAI.Application.WebChat.SetWebChatEndpointStatus;
using OmniAI.Application.WebChat.UpdateWebChatWidget;
using OmniAI.Application.WebChat;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Application.Tenants.RenameTenant;
using OmniAI.Application.Tenants.ListMyTenants;
using OmniAI.Application.Tenants;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Time;
using OmniAI.Infrastructure.Security;
using OmniAI.Infrastructure.WebChat.Protection;

namespace OmniAI.Api.Composition;

/// <summary>
/// Wires the application together.
/// </summary>
/// <remarks>
/// Exists so <c>Program.cs</c> stays a readable list of pipeline steps rather
/// than growing a new registration line for every use case the platform gains.
/// <para>
/// Every registration is explicit. No assembly scanning, no reflection-based
/// handler discovery and no mediator: a scanner would replace a readable list
/// with a convention that has to be learned, and would happily register or miss
/// a type without anyone noticing. Eighteen handlers is still a list worth
/// reading -- and the day it is not, an explicit list that has grown too long is
/// a better problem than a convention nobody can audit.
/// </para>
/// </remarks>
internal static class CompositionRoot
{
    /// <summary>
    /// Configuration key holding the platform database connection string.
    /// </summary>
    /// <remarks>
    /// Supplied by the environment -- an environment variable, user secrets, or a
    /// deployment secret store. It is deliberately blank in
    /// <c>appsettings.json</c>: a real connection string in source control is a
    /// committed credential, and a plausible-looking default is worse, because a
    /// misconfigured deployment would silently connect somewhere.
    /// </remarks>
    private const string DatabaseConnectionName = "OmniAiDatabase";

    public static IServiceCollection AddOmniAi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Phase 37.2: the local AI stand-in is a developer-machine tool. Asked for
        // anywhere else, or alongside the live provider, the process refuses to
        // start here, before anything is built. The runtime registration below
        // checks again, so no path composes it without this rule.
        _ = LocalAiStandInSwitch.IsEnabled(configuration, environment.IsDevelopment());

        // Phase 35: a deployment missing what it cannot run correctly without
        // refuses to start, naming the keys, instead of degrading at first use.
        ProductionConfiguration.Validate(configuration, environment);

        // How this process learns the scheme the browser actually used, across a
        // TLS-terminating proxy, and which proxies it will believe. Registered
        // before authentication because every cookie decision depends on it.
        services.AddOmniAiForwardedHeaders(configuration);

        // Authentication, authorization, antiforgery and the current-user
        // adapter. Registered before the use cases that depend on knowing who
        // is calling. The environment is passed in because cookie security is
        // the one setting that must differ between Development and everywhere
        // else -- see AuthenticationSetup for exactly how far that goes.
        services.AddOmniAiAuthentication(configuration, environment);

        // Infrastructure decides how persistence is built; the composition root
        // only decides where its configuration comes from.
        // Phase 35: every pool sized and every database wait bounded explicitly,
        // and outside Development the declared connection budget must hold.
        DatabaseConnectionPolicy databasePolicy = DatabaseConnectionPolicy.FromConfiguration(configuration);
        databasePolicy.Validate(requireBudget: !environment.IsDevelopment());

        services.AddOmniAiPersistence(configuration.GetConnectionString(DatabaseConnectionName), databasePolicy);

        // Authenticated encryption for the credentials this platform stores on a
        // workspace's behalf. The ephemeral key is allowed in Development only:
        // it makes the feature usable on a laptop without a key ceremony, and it
        // is worthless for anything else because it is discarded on restart.
        // Everywhere else a missing key means credentials cannot be stored at
        // all, which is the safe failure.
        services.AddOmniAiSecretProtection(
            configuration,
            allowEphemeralKey: environment.IsDevelopment());

        // After secret protection: the key ring is encrypted with the same master key.
        services.AddOmniAiDataProtection(configuration);

        // The WhatsApp channel: storage, the Meta transport, webhook security
        // and the two background workers.
        //
        // Development is passed for the same reason it is passed to the AI
        // runtime below: a machine that has been pointed at a real provider once
        // keeps that setting, and the accident here is worse than a bill -- it is
        // a message in a real person's WhatsApp. Nothing outside Development
        // requires the opt-in, so a configured deployment cannot be silenced by a
        // forgotten flag.
        services.AddOmniAiWhatsApp(
            configuration,
            requireLiveProviderOptIn: environment.IsDevelopment());

        // Subscriptions. Registered on every deployment, including one with no
        // provider keys: the billing screen must be able to say that nothing is
        // for sale rather than fail to load.
        services.AddOmniAiBilling(configuration);

        // Reports: exports and schedules over the same reads the dashboard uses.
        // Storage comes from configuration and is refused if it points inside the
        // application; absent, reports simply cannot be generated here.
        services.AddOmniAiReports(configuration, environment.ContentRootPath);

        services.AddOmniAiNotifications(
            configuration,
            requireLiveEmailOptIn: environment.IsDevelopment());

        services.AddOmniAiReliability(configuration);

        // The clock. TimeProvider is the BCL abstraction, so no third-party clock
        // package is involved and nothing in the Domain or Application had to invent
        // one. Tests substitute a deterministic instance, which is why no handler
        // calls DateTimeOffset.UtcNow itself. Singleton because it holds no
        // per-request state.
        //
        // Wrapped rather than registered raw: DateTimeOffset counts 100ns ticks and
        // PostgreSQL timestamptz stores microseconds, so an unwrapped system clock
        // hands out instants three digits finer than anything that can be written.
        // A create response would then report a createdAt that differs from the one
        // the very next read returns. See MicrosecondTimeProvider.
        services.AddSingleton<TimeProvider>(new MicrosecondTimeProvider(TimeProvider.System));

        // Public chat protection: the distributed rate limits in front of the one
        // surface where an anonymous stranger can create data and spend money.
        //
        // Infrastructure decides what the counters are and where they live -- this
        // root names no store, no algorithm and no cryptographic primitive. It
        // decides only where configuration comes from, and one thing Infrastructure
        // deliberately has no opinion about: whether a missing network-key secret
        // may be replaced by a random per-process one. That is true in Development
        // and nowhere else, so that a developer who has just cloned the repository
        // gets a working hosted chat without first being told about a setting.
        //
        // It is not a protection bypass and must not become one: every limit is
        // enforced in full either way, and the only difference is that the buckets
        // are renamed when the process restarts.
        services.AddOmniAiPublicChatProtection(
            configuration,
            allowEphemeralNetworkKeySecret: environment.IsDevelopment());

        // Phase 37.3: the account limits (sign-in, reset requests, resets) share
        // that Redis and that key secret, so every instance counts the same
        // attempts. Then the account services and their email outbox.
        services.AddOmniAiAccountProtection(configuration);
        services.AddOmniAiAccounts();

        // Realtime. Registered here because it shares the protection's Redis, and
        // for no other reason: the two have nothing to say to each other.
        //
        // SignalR itself is part of the framework. The backplane is not, and
        // adding it is the difference between an inbox that updates and one that
        // updates for whichever half of the staff happened to land on the
        // instance that handled the write. That failure is invisible on one
        // instance, which is exactly where it would be tested.
        string? realtimeRedis = configuration.GetConnectionString(
            PublicChatProtectionSetup.RedisConnectionName);

        services.AddOmniAiRealtimeTickets(realtimeRedis);

        // Phase 37.3: each instance knows its admin hub connections by account, so a
        // password reset can close them, and every invocation re-checks the stamp.
        services.AddSingleton<SessionConnections>();
        services.AddSingleton<SessionStampHubFilter>();
        services.AddSingleton<ISessionRevocation, SessionRevocation>();
        services.AddHostedService<SessionRevocationListener>();

        ISignalRServerBuilder realtime = services.AddSignalR(options => options.AddFilter<SessionStampHubFilter>());

        if (!string.IsNullOrWhiteSpace(realtimeRedis))
        {
            realtime.AddStackExchangeRedis(realtimeRedis, options =>
            {
                // Namespaced, so a Redis shared with anything else -- including
                // another deployment of this platform -- cannot deliver one
                // workspace's events to another's browsers.
                options.Configuration.ChannelPrefix =
                    StackExchange.Redis.RedisChannel.Literal("omniai:realtime");
            });
        }

        services.AddScoped<IConversationRealtimeNotifier, SignalRConversationNotifier>();
        services.AddScoped<INotificationRealtime, SignalRNotificationRealtime>();
        services.AddScoped<IConversationReadRealtime, SignalRConversationReadRealtime>();
        services.AddScoped<IKnowledgeRealtimeNotifier, SignalRKnowledgeNotifier>();

        // The AI runtime. Infrastructure decides which implementation that is:
        // the OpenAI adapter when a key is configured in the environment, and an
        // implementation that refuses every request explicitly when it is not.
        // The composition root only says where non-secret configuration comes
        // from -- it names no vendor, reads no key, and does not know which of the
        // two it just registered.
        //
        // In Development it says one thing more, and for the same reason it says
        // it about the network-key secret above: a developer machine is not a
        // deployment. A key inherited from the shell that started the process is
        // not a decision anybody made, and treating it as one has already cost
        // real money once. Development therefore requires a deliberate opt-in
        // alongside the key; production does not, and must not.
        //
        // Development may also run the local AI stand-in instead, when
        // OMNIAI_DEV_AI_STANDIN=1 asks for it (Phase 37.2).
        services.AddOmniAiRuntime(
            configuration,
            requireLiveProviderOptIn: environment.IsDevelopment(),
            allowLocalStandIn: environment.IsDevelopment());

        // Application use cases. Registered as their concrete types, because
        // nothing substitutes them at runtime -- an interface per handler would be
        // ceremony with one implementation each. Scoped, so a handler shares the
        // request's DbContext and therefore its unit of work.
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<GetTenantByIdHandler>();
        services.AddScoped<RenameTenantHandler>();
        services.AddScoped<ChangeTenantTimeZoneHandler>();
        services.AddScoped<ListMyTenantsHandler>();

        // The two halves of the authorization boundary. The guard answers "does
        // this person belong to this workspace"; the authorizer adds "and may
        // they do this", composing the guard with the roles they hold. Both are
        // scoped, like the handlers that hold them, so they share the request's
        // DbContext and its view of the current principal.
        //
        // Registered as concrete types with no interface between them, which is
        // deliberate: nothing substitutes either at runtime, and an interface
        // would make it possible to register a permissive implementation by
        // accident -- for the authorizer, that is a platform-wide authorization
        // bypass in one line.
        services.AddScoped<TenantAccessGuard>();
        services.AddScoped<TenantAuthorizer>();

        // Role administration.
        services.AddScoped<ListTenantRolesHandler>();
        services.AddScoped<GetTenantRoleByIdHandler>();
        services.AddScoped<CreateTenantRoleHandler>();
        services.AddScoped<RenameTenantRoleHandler>();
        services.AddScoped<ReplaceTenantRolePermissionsHandler>();
        services.AddScoped<GetPermissionCatalogHandler>();
        services.AddScoped<GetMyTenantPermissionsHandler>();

        // Team administration.
        services.AddScoped<ListTenantMembersHandler>();
        services.AddScoped<GetTenantMemberByIdHandler>();
        services.AddScoped<GetAssignableRolesHandler>();
        services.AddScoped<ReplaceTenantMemberRolesHandler>();
        services.AddScoped<ListTenantInvitationsHandler>();
        services.AddScoped<CreateTenantInvitationHandler>();
        services.AddScoped<RevokeTenantInvitationHandler>();
        services.AddScoped<AcceptTenantInvitationHandler>();

        // How long an invitation stays open. A singleton value rather than a
        // constant read at each use site, so the lifetime is configurable and so a
        // test can issue one that has already expired without waiting a week.
        services.AddSingleton(new InvitationPolicy());

        // Website chat administration.
        services.AddScoped<ListWebChatEndpointsHandler>();
        services.AddScoped<GetWebChatEndpointByIdHandler>();
        services.AddScoped<CreateWebChatEndpointHandler>();
        services.AddScoped<SetWebChatEndpointStatusHandler>();

        // The hosted chat runtime.
        services.AddScoped<GetWebChatWidgetHandler>();
        services.AddScoped<UpdateWebChatWidgetHandler>();
        services.AddScoped<EmbeddedWebChatHandler>();

        services.AddScoped<PublicWebChatAccess>();
        services.AddScoped<StartWebChatConversationHandler>();
        services.AddScoped<SendWebChatMessageHandler>();
        services.AddScoped<ListWebChatMessagesHandler>();

        // Who the workspace talks to. Registered beside conversations rather than
        // inside a channel, because a customer belongs to none of them. Its
        // repository is composed by Infrastructure, which owns the type.
        services.AddScoped<OmniAI.Application.Customers.CustomerHandlers>();

        // Reading conversations from the admin.
        services.AddScoped<ReadConversationsHandler>();
        services.AddScoped<SocialSimulator>();
        services.AddScoped<QuickReplyHandler>();
        services.AddScoped<InboxRoutingHandler>();
        services.AddScoped<ConversationNotesHandler>();
        services.AddScoped<ConversationHandoffHandler>();
        services.AddScoped<ConversationAssignmentHandler>();
        services.AddScoped<ReplyToConversationHandler>();
        services.AddScoped<MarkConversationReadHandler>();
        services.AddScoped<MarkAllConversationsReadHandler>();

        // The one orchestration both channels enter. Website chat and WhatsApp
        // hand it a customer turn and get back the same outcome; neither has a
        // copy of the lock, the replay check, retrieval or generation.
        services.AddScoped<CustomerTurnRunner>();

        // WhatsApp.
        services.AddScoped<WhatsAppConnectionHandlers>();
        services.AddScoped<ReceiveWhatsAppWebhookHandler>();
        services.AddScoped<ProcessInboundWhatsAppHandler>();
        services.AddScoped<DispatchWhatsAppDeliveryHandler>();
        services.AddScoped<ReadWhatsAppConversationHandler>();

        // What happens to a reply after it is stored, per channel. Website chat
        // needs nothing -- the visitor is reading the transcript -- so the only
        // implementation is the one that queues a WhatsApp delivery, and it is
        // silent for every conversation that did not come from there.
        services.AddScoped<IOutboundReplyDispatcher, WhatsAppReplyDispatcher>();
        services.AddScoped<IssueRealtimeTicketHandler>();

        // Resource policies. Singletons rather than constants read at each use
        // site, so the limits are configurable and a test can drive a tiny window
        // without waiting for a real conversation to grow.
        services.AddSingleton(new WebChatPolicy());
        services.AddSingleton(new ConversationContextPolicy());
        services.AddSingleton(new ConversationReadPolicy());
        services.AddScoped<CreateAiAgentHandler>();
        services.AddScoped<GetAiAgentByIdHandler>();
        services.AddScoped<ListAiAgentsHandler>();
        services.AddScoped<RenameAiAgentHandler>();
        services.AddScoped<UpdateAiAgentInstructionsHandler>();
        services.AddScoped<ActivateAiAgentHandler>();
        services.AddScoped<DisableAiAgentHandler>();
        services.AddScoped<PreviewAiAgentHandler>();

        services.AddScoped<ListKnowledgeDocumentsHandler>();
        services.AddScoped<GetKnowledgeDocumentByIdHandler>();
        services.AddScoped<CreateKnowledgeDocumentHandler>();
        services.AddScoped<UpdateKnowledgeDocumentHandler>();
        services.AddScoped<IndexKnowledgeDocumentHandler>();
        services.AddScoped<SetKnowledgeDocumentStatusHandler>();
        services.AddScoped<GetAgentKnowledgeHandler>();
        services.AddScoped<ReplaceAgentKnowledgeHandler>();
        services.AddScoped<SearchAgentKnowledgeHandler>();

        services.AddScoped<KnowledgeRetriever>();

        // Uploading business files into Knowledge.
        services.AddScoped<GetWorkspaceAnalyticsHandler>();
        services.AddScoped<GetWorkspacePlanUsageHandler>();

        // Billing. Reading what a workspace pays, starting a hosted checkout,
        // opening the provider's portal, and applying what the provider reports.
        services.AddScoped<GetWorkspaceBillingHandler>();
        services.AddScoped<StartBillingCheckoutHandler>();
        services.AddScoped<CreateBillingPortalSessionHandler>();
        services.AddScoped<ReceiveBillingWebhookHandler>();
        services.AddScoped<ApplyProviderBillingEvent>();

        services.AddScoped<UploadKnowledgeFileHandler>();
        services.AddScoped<ReplaceKnowledgeSourceHandler>();

        // What this platform will accept from an upload and how far it will read.
        // A singleton like every other policy here, so a deployment can raise a
        // bound deliberately and a test can drive a tiny one.
        services.AddSingleton(new KnowledgeFilePolicy());

        // Singletons because they are pure policy: no state, no clock, no
        // connection. The chunker in particular is a pure function of its input,
        // which is what makes an index reproducible.
        services.AddSingleton(new KnowledgeChunkingPolicy());
        services.AddSingleton(new KnowledgeRetrievalPolicy());
        services.AddSingleton<KnowledgeChunker>();

        // The antiforgery filter. Registered so route groups can resolve it by
        // type; it is applied at the mapping sites rather than globally, so a
        // group without it is visible in the routing code.
        services.AddScoped<AntiforgeryEndpointFilter>();

        // Turns expected Domain and Application failures into Problem Details.
        // Registered before any future handler so its specific cases are
        // considered first.
        services.AddExceptionHandler<DomainProblemDetailsHandler>();

        return services;
    }
}
