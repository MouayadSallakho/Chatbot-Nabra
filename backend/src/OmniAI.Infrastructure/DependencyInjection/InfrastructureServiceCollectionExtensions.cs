using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Analytics;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Plans;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Invitations;
using OmniAI.Application.Members.Persistence;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Infrastructure.Analytics;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Conversations;
using OmniAI.Infrastructure.Invitations;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Knowledge.Files;
using OmniAI.Infrastructure.WebChat;
using OmniAI.Infrastructure.Ai;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Plans;
using System.Globalization;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Infrastructure implementations of Application-owned contracts.
/// </summary>
/// <remarks>
/// Registration lives beside the implementations, so the composition root does
/// not need to know that persistence is EF Core, that the provider is Npgsql, or
/// which concrete types satisfy <see cref="ITenantRepository"/>,
/// <see cref="IAiAgentRepository"/> and <see cref="IAiTextGenerator"/>. That last
/// one is the point of the whole arrangement: the API can offer an AI capability
/// without any layer above this file knowing whether a provider exists, let alone
/// which.
/// <para>
/// Written by hand rather than by assembly scanning. A short explicit list
/// does not justify a convention-based scanner, and it is the one form of this
/// code that cannot silently register -- or silently miss -- a type.
/// </para>
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the PostgreSQL persistence layer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// The database connection string. It is a parameter rather than something
    /// read from <c>IConfiguration</c> here, because deciding where configuration
    /// comes from is the composition root's job -- and it keeps this project free
    /// of a configuration dependency it does not otherwise need.
    /// <para>
    /// May be blank. The provider is then configured without a connection string,
    /// so the application still starts and any endpoint that needs no database
    /// still works, while the first actual database call fails with EF Core's own
    /// explicit "connection string has not been initialized" error. That is
    /// deliberate: a misconfigured deployment should fail where it is used, with a
    /// message that says what is missing, rather than be papered over with a
    /// fabricated default that quietly points somewhere real.
    /// </para>
    /// </param>
    /// <param name="policy">
    /// Pool sizes and timeouts (Phase 35). Applied to every pool this method
    /// creates, overriding anything the connection string says, so the declared
    /// connection budget is the one actually in force.
    /// </param>
    public static IServiceCollection AddOmniAiPersistence(
        this IServiceCollection services,
        string? connectionString,
        DatabaseConnectionPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        DatabaseConnectionPolicy connections = policy ?? new DatabaseConnectionPolicy();

        services.AddSingleton(connections);

        services.AddDbContext<OmniAiDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseNpgsql(connections.ApplicationConnectionString(connectionString));
            }
        });

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IWorkspaceTimeZones, WorkspaceTimeZones>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<ITenantRoleRepository, TenantRoleRepository>();
        services.AddScoped<ITenantMemberRepository, TenantMemberRepository>();
        services.AddScoped<ITenantInvitationRepository, TenantInvitationRepository>();
        services.AddScoped<IAiAgentRepository, AiAgentRepository>();
        services.AddScoped<IAiAgentChannelReader, AiAgentChannelReader>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IAgentKnowledgeRepository, AgentKnowledgeRepository>();
        services.AddScoped<IKnowledgeIndexStore, KnowledgeIndexStore>();
        services.AddScoped<IKnowledgeIndexAttemptStore, KnowledgeIndexAttemptStore>();
        services.AddScoped<IKnowledgeSearchStore, KnowledgeSearchStore>();
        services.AddScoped<IKnowledgeSourceRepository, KnowledgeSourceRepository>();

        // Analytics reads the operational tables directly rather than through the
        // repositories, because a repository answers "give me this aggregate" and
        // a dashboard asks "count these rows". Scoped, like every other consumer
        // of the DbContext.
        services.AddScoped<IWorkspaceAnalyticsReader, WorkspaceAnalyticsReader>();

        // Entitlements count the operational tables too, and hold a transaction
        // across the count and the mutation, so they share the caller's scoped
        // DbContext deliberately -- that is what makes the two one atomic step.
        services.AddScoped<IWorkspaceEntitlementService, WorkspaceEntitlementService>();
        services.AddScoped<IPlanCatalog, PlanCatalog>();
        services.AddScoped<DefaultPlanResolver>();

        // Reading text out of uploaded files. A singleton because it holds only
        // the limits: every parser call is a pure function of the bytes handed to
        // it, and nothing is cached between uploads -- the file does not outlive
        // the request that carried it.
        services.AddSingleton<IKnowledgeFileExtractor>(
            provider => new KnowledgeFileExtractor(
                provider.GetRequiredService<KnowledgeFilePolicy>()));

        // The one account fact the product may read. Scoped, because it reads
        // through the request's DbContext.
        services.AddScoped<IAccountDirectory, IdentityAccountDirectory>();

        // Minting invitation secrets holds no state and touches no request:
        // RandomNumberGenerator and SHA-256 are both static and thread-safe, so a
        // single instance serves every caller.
        services.AddSingleton<IInvitationTokens, InvitationTokens>();

        // Website chat.
        services.AddScoped<IWebChatEndpointRepository, WebChatEndpointRepository>();
        services.AddScoped<IWebChatRuntimeStore, WebChatRuntimeStore>();
        services.AddScoped<IConversationRepository, ConversationRepository>();

        // The canonical customer directory. Scoped like every other repository:
        // it holds the request's DbContext and nothing else.
        services.AddScoped<
            OmniAI.Application.Customers.Persistence.ICustomerRepository,
            CustomerRepository>();
        services.AddScoped<IConversationInboxStore, ConversationInboxStore>();
        services.AddScoped<IQuickReplyStore, QuickReplyStore>();
        services.AddScoped<IInboxRoutingStore, InboxRoutingStore>();
        services.AddScoped<IConversationNoteStore, ConversationNoteStore>();
        services.AddScoped<ISocialSimulatorStore, SocialSimulatorStore>();

        // Both mint values, neither holds state, and the primitives behind them
        // are static and thread-safe -- so one instance serves every caller. They
        // are separate services because one produces something published and the
        // other something secret, and the call sites must not be interchangeable.
        services.AddSingleton<IWebChatPublicKeys, WebChatPublicKeys>();
        services.AddSingleton<IWebChatAccessTokens, WebChatAccessTokens>();

        // The conversation lock needs a connection of its own -- one it can hold
        // across a provider call without holding a transaction -- so it takes a
        // data source rather than the request's DbContext.
        //
        // Registered unconditionally, and built lazily, for the same reason the
        // DbContext above tolerates a blank connection string: a deployment with
        // no database configured must still start, so that endpoints needing no
        // database still work and the failure appears where the database is
        // actually used, naming what is missing. Registering this only when a
        // connection string was supplied would instead fail the whole application
        // at startup with a dependency-injection error that says nothing about
        // configuration.
        services.AddSingleton(_ => string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                "Website chat needs a database connection string. Set "
                + "ConnectionStrings:OmniAiDatabase.")
            : NpgsqlDataSource.Create(connections.ConversationLockConnectionString(connectionString)));

        services.AddScoped<IConversationLock, PostgresConversationLock>();

        return services;
    }

    /// <summary>
    /// Adds the AI runtime: OpenAI when a key is configured, an explicit refusal
    /// when it is not.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration carrying the non-secret <c>OpenAI</c> section. The API key is
    /// deliberately not read from here.
    /// </param>
    /// <remarks>
    /// <para>
    /// Separate from persistence, because it is a separate capability with a
    /// separate reason to change: a deployment can have a database and no AI
    /// provider, and that is a supported state rather than a broken one.
    /// </para>
    /// <para>
    /// <strong>The key's presence is what decides.</strong> With
    /// <c>OPENAI_API_KEY</c> set, <see cref="IAiTextGenerator"/> resolves to the
    /// OpenAI adapter. Without it, it resolves to
    /// <see cref="UnconfiguredAiTextGenerator"/> and every generation attempt
    /// fails explicitly — the accepted behaviour, unchanged. Either way the
    /// application starts, health checks answer and the whole agent management
    /// slice works: a developer who has not configured OpenAI must not be met
    /// with an application that refuses to run.
    /// </para>
    /// <para>
    /// <strong>The key is read here and nowhere else.</strong> It is read as
    /// <c>OPENAI_API_KEY</c> through <see cref="IConfiguration"/>, which in the
    /// hosted application is supplied by the environment-variable provider — so
    /// in production this is the environment variable and nothing else. Going
    /// through configuration rather than <c>Environment.GetEnvironmentVariable</c>
    /// is what makes this method testable: a test can prove both branches without
    /// mutating the process's environment, which is global state that would race
    /// across parallel test classes.
    /// </para>
    /// <para>
    /// It is handed straight to the client factory: not bound into
    /// <see cref="OpenAiOptions"/>, not stored in a field of ours, not logged,
    /// not included in any exception. Only its presence is ever tested. It must
    /// never be placed in <c>appsettings</c>, and a test asserts that no settings
    /// file in the repository contains it.
    /// </para>
    /// <para>
    /// Both implementations are singletons. The OpenAI client is documented as
    /// reusable and thread-safe, and it owns an HTTP stack: building one per
    /// request would exhaust sockets under any real load.
    /// </para>
    /// <para>
    /// <strong>In Development the key is necessary but not sufficient.</strong>
    /// See <paramref name="requireLiveProviderOptIn"/>.
    /// </para>
    /// </remarks>
    /// <param name="requireLiveProviderOptIn">
    /// <para>
    /// When <see langword="true"/>, a configured key alone does not compose the
    /// OpenAI adapters: <see cref="OpenAiOptions.LiveProviderOptInVariable"/> must
    /// also be exactly <see cref="OpenAiOptions.LiveProviderOptInValue"/>.
    /// </para>
    /// <para>
    /// <strong>This exists because a key was inherited once and cost real
    /// money.</strong> A developer machine commonly has <c>OPENAI_API_KEY</c> in
    /// its environment for some other project; anything started from that shell
    /// inherits it, so a verification run intended to exercise the
    /// provider-unconfigured path quietly made a live request instead — and the
    /// only symptom was that it succeeded. Unsetting the variable by hand for one
    /// session is not a safeguard, because the next session starts from the same
    /// shell.
    /// </para>
    /// <para>
    /// <strong>The composition root passes Development here and nothing
    /// else.</strong> Production must not require a second flag: a deployment that
    /// has deliberately configured a provider would be taken offline by somebody
    /// forgetting one, and this is a cost guard for developer machines, not a
    /// security control for deployments.
    /// </para>
    /// </param>
    /// <param name="allowLocalStandIn">
    /// <para>
    /// Whether this host may run the local AI stand-in (Phase 37.2). The
    /// composition root passes Development here and nothing else.
    /// </para>
    /// <para>
    /// The stand-in is composed only when this is <see langword="true"/> and
    /// <see cref="LocalAiStandInSwitch.Variable"/> is exactly
    /// <see cref="LocalAiStandInSwitch.EnabledValue"/>. The variable on a host
    /// that may not run it, with any other value, or together with the live
    /// OpenAI opt-in stops startup; see <see cref="LocalAiStandInSwitch"/>.
    /// Without the variable nothing here changes.
    /// </para>
    /// </param>
    public static IServiceCollection AddOmniAiRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireLiveProviderOptIn = false,
        bool allowLocalStandIn = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Decided first, so a refused combination stops the process before
        // anything is registered.
        bool standIn = LocalAiStandInSwitch.IsEnabled(configuration, allowLocalStandIn);

        string? apiKey = configuration[OpenAiOptions.ApiKeyVariable];

        // Read through IConfiguration exactly as the key is, so a test can prove
        // every combination without mutating the process environment -- which is
        // global state shared with every other test running in parallel.
        //
        // An exact match on "1". Not "true", not "yes", not "any non-empty value":
        // a guard whose vocabulary is generous is a guard somebody defeats by
        // accident, which is the failure it exists to prevent.
        bool optedIn = string.Equals(
            configuration[OpenAiOptions.LiveProviderOptInVariable],
            OpenAiOptions.LiveProviderOptInValue,
            StringComparison.Ordinal);

        if (requireLiveProviderOptIn && !optedIn)
        {
            // A key is present but has not been opted into. Both providers stay
            // unconfigured -- the text generator and the embedding generator, not
            // one of them -- and the application behaves exactly as it does on a
            // machine with no key at all: the existing explicit refusal, no
            // outbound request, and nothing spent.
            apiKey = null;
        }

        // Accounting is registered whatever the provider situation is. A
        // deployment with no key still gets a ledger and a (possibly empty) rate
        // card, so nothing downstream has to ask whether accounting exists.
        //
        // The catalogue is read and validated here, at startup: a misspelled model
        // or a negative rate should stop a deployment from starting, not appear a
        // month later as a cost report nobody can explain.
        //
        // The stand-in's zero rate is added only with the stand-in itself.
        services.AddSingleton<IAiProviderPricing>(
            AiProviderPricingCatalog.Create(ReadPricing(configuration), includeLocalStandIn: standIn));

        services.AddSingleton<IAiProviderUsageLedger, AiProviderUsageLedger>();

        // Which plan a new workspace starts on. Bound but deliberately not
        // validated here: an empty value is a perfectly startable deployment that
        // simply cannot create workspaces, and failing at startup would take down
        // a running system over a setting only onboarding needs.
        services.AddSingleton(new DefaultPlanOptions
        {
            DefaultPlanCode =
                configuration[$"{DefaultPlanOptions.Section}:{nameof(DefaultPlanOptions.DefaultPlanCode)}"]
                ?? string.Empty,
        });

        // A clock, if the host has not already chosen one. The ledger stamps
        // every operation, so this assembly now depends on one whether or not it
        // is composed into the API -- and a host that registered its own keeps it,
        // because TryAdd yields.
        services.TryAddSingleton(TimeProvider.System);

        // The embedding profile is registered whichever branch is taken. How this
        // deployment is configured to make vectors is a fact about the deployment,
        // not about whether a credential happens to be present -- and without it
        // the knowledge screens could not report readiness at all on a deployment
        // with no key, which is exactly the deployment somebody is most likely to
        // be looking at first.

        EmbeddingOptions embeddingOptions = ReadEmbeddingOptions(configuration);

        services.AddSingleton(embeddingOptions);
        services.AddSingleton(EmbeddingProfile.Create(
            standIn ? LocalAiStandInOptions.ProfileId : embeddingOptions.ProfileId,
            EmbeddingOptions.Dimensions,
            embeddingOptions.MaxBatchSize,
            embeddingOptions.MaxInputCharacters));

        if (standIn)
        {
            // The local stand-in, for both capabilities: in process, no network,
            // no key. Its vectors carry their own profile, so a document indexed
            // here reads as needing an index once a real provider is configured.
            services.AddSingleton(LocalAiStandInOptions.Read(configuration));
            services.AddSingleton<IAiTextGenerator, LocalStandInTextGenerator>();
            services.AddSingleton<ITextEmbeddingGenerator, LocalStandInEmbeddingGenerator>();

            return services;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No provider. The honest refusal, exactly as before -- and now for
            // embeddings too, which means a document can never become ready
            // without a real index behind it.
            services.AddSingleton<IAiTextGenerator, UnconfiguredAiTextGenerator>();
            services.AddSingleton<ITextEmbeddingGenerator, UnconfiguredTextEmbeddingGenerator>();

            return services;
        }

        // Read explicitly rather than through the configuration binder, which
        // would mean adding a package for three settings. Being explicit also
        // means an unrecognised key is visibly not read, instead of silently
        // ignored by a reflective bind.
        IConfigurationSection section = configuration.GetSection(OpenAiOptions.SectionName);

        OpenAiOptions options = new()
        {
            Model = Setting(section, nameof(OpenAiOptions.Model)) ?? OpenAiOptions.DefaultModel,
            MaxOutputTokens = Number(
                section,
                nameof(OpenAiOptions.MaxOutputTokens),
                OpenAiOptions.DefaultMaxOutputTokens),
            TimeoutSeconds = Number(
                section,
                nameof(OpenAiOptions.TimeoutSeconds),
                OpenAiOptions.DefaultTimeoutSeconds),
        };

        // Checked now rather than on the first request, so a mistyped setting is a
        // startup failure an operator sees rather than a runtime failure a
        // customer discovers.
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(OpenAiClientFactory.Create(apiKey, options));
        services.AddSingleton<IAiTextGenerator, OpenAiTextGenerator>();

        services.AddSingleton(OpenAiEmbeddingClientFactory.Create(apiKey, embeddingOptions));
        services.AddSingleton<ITextEmbeddingGenerator, OpenAiTextEmbeddingGenerator>();


        return services;
    }

    /// <summary>
    /// Reads the configured provider rate card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound from <c>AiPricing:Rates</c>. It is ordinary configuration, not a
    /// secret: a published rate card needs no protection, and treating it as
    /// sensitive would only make it harder to keep current.
    /// </para>
    /// <para>
    /// Absent configuration is a valid deployment. Every operation is then
    /// unrated, which is reported honestly rather than shown as free.
    /// </para>
    /// </remarks>
    private static List<AiModelRateOptions> ReadPricing(IConfiguration configuration)
    {
        List<AiModelRateOptions> rates = [];

        configuration.GetSection(PricingSectionName).Bind(rates);

        return rates;
    }

    /// <summary>Where the rate card is read from.</summary>
    private const string PricingSectionName = "AiPricing:Rates";

    /// <summary>
    /// Reads the embedding settings, validating them at startup.
    /// </summary>
    /// <remarks>
    /// The dimension is deliberately not among them. It is a constant, because
    /// the database column is declared for it: a settings value that disagreed
    /// would fail on the first insert, deep inside an index transaction, with a
    /// message about a column type rather than about configuration.
    /// </remarks>
    private static EmbeddingOptions ReadEmbeddingOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(EmbeddingOptions.SectionName);

        EmbeddingOptions options = new()
        {
            Model = Setting(section, nameof(EmbeddingOptions.Model))
                ?? EmbeddingOptions.DefaultModel,
            MaxBatchSize = Number(
                section,
                nameof(EmbeddingOptions.MaxBatchSize),
                EmbeddingOptions.DefaultMaxBatchSize),
            MaxInputCharacters = Number(
                section,
                nameof(EmbeddingOptions.MaxInputCharacters),
                EmbeddingOptions.DefaultMaxInputCharacters),
            TimeoutSeconds = Number(
                section,
                nameof(EmbeddingOptions.TimeoutSeconds),
                EmbeddingOptions.DefaultTimeoutSeconds),
        };

        options.Validate();

        return options;
    }

    /// <summary>A configured string, or null when it is absent or blank.</summary>
    private static string? Setting(IConfigurationSection section, string key)
    {
        string? value = section[key];

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// A configured whole number, or the supplied default when it is absent.
    /// </summary>
    /// <remarks>
    /// A present but unparseable value is an error rather than a silent fallback.
    /// Quietly substituting a default would mean a deployment that meant to cap
    /// spending at 200 tokens, and mistyped it, ran at whatever this code happens
    /// to prefer without anyone being told.
    /// </remarks>
    private static int Number(IConfigurationSection section, string key, int fallback)
    {
        string? value = Setting(section, key);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{section.Key}:{key} must be a whole number.");
    }
}
