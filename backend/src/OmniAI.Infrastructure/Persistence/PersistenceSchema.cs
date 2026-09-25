namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Names that the schema and the queries must agree on.
/// </summary>
internal static class PersistenceSchema
{
    /// <summary>
    /// Shadow property holding the value that orders a message within its
    /// conversation. Not a Domain concept: the Domain's identity is
    /// <c>MessageId</c>, and nothing in the Domain reads this.
    /// </summary>
    /// <remarks>
    /// It exists because reload order cannot be derived from anything else.
    /// <c>Message.SentAt</c> is deliberately not monotonic, so ordering by it
    /// would reorder history. <c>MessageId</c> cannot serve either: it is a
    /// UUIDv7, which orders only to the millisecond and carries random bits
    /// below that, so messages appended within the same millisecond sort
    /// arbitrarily.
    /// <para>
    /// The value is allocated from <see cref="MessageIngestionSequenceName"/>,
    /// a PostgreSQL sequence, by <c>OmniAiDbContext</c> immediately before
    /// insert. Only relative order is meaningful; the numbers are global rather
    /// than per-conversation, so they are sparse within any one conversation and
    /// gaps are expected.
    /// </para>
    /// </remarks>
    public const string MessageIngestionSequence = "IngestionSequence";

    /// <summary>
    /// The PostgreSQL sequence that allocates message ingestion order.
    /// </summary>
    /// <remarks>
    /// A sequence rather than a counter on the conversation row: <c>nextval</c>
    /// is atomic, takes no row lock, never returns a value twice even across
    /// concurrent sessions, and does not touch the conversation row -- so it
    /// cannot disturb that row's <c>xmin</c> concurrency token. A per-conversation
    /// counter column would have to be updated on every append, which would bump
    /// the conversation's <c>xmin</c> and make unrelated writers fail.
    /// </remarks>
    public const string MessageIngestionSequenceName = "message_ingestion_sequence";

    /// <summary>
    /// Shadow property holding the case-insensitive form of a role's name, which
    /// the tenant-scoped unique index is built on.
    /// </summary>
    /// <remarks>
    /// A shadow property rather than a member of <c>TenantRoleName</c>'s mapping,
    /// because a value converter fills one column and this name needs two: the
    /// display form a person reads, and the form uniqueness is decided on. The
    /// role repository sets it from <c>TenantRoleName.Normalised</c> on every
    /// write, so normalisation has exactly one implementation -- the Domain's --
    /// rather than one in .NET and a second in PostgreSQL's <c>upper()</c>, which
    /// are not guaranteed to agree for non-ASCII names under every collation.
    /// </remarks>
    public const string NormalisedRoleName = "NormalisedName";

    /// <summary>
    /// The shadow property holding an invited address in its comparison form.
    /// </summary>
    /// <remarks>
    /// A shadow property for the same reason the normalised role name is one: the
    /// Domain already derives it, and a second column the Domain also knew about
    /// would be two places that could disagree about what canonical means.
    /// </remarks>
    public const string CanonicalEmail = "CanonicalEmail";

    /// <summary>
    /// PostgreSQL's per-row transaction id system column, used as an optimistic
    /// concurrency token. It already exists on every table; it is never created
    /// by a migration.
    /// </summary>
    public const string XminConcurrencyToken = "xmin";

    /// <summary>
    /// Shadow property holding the fingerprint of a knowledge document's current
    /// content.
    /// </summary>
    /// <remarks>
    /// A column because the retrieval query compares it against the indexed
    /// fingerprint inside the database. That comparison is what keeps an edited
    /// document out of every search until it is indexed again, and it cannot be
    /// done in memory without loading every document's full text on every turn.
    /// Shadow rather than a property on the aggregate, because the aggregate
    /// derives it from its content and must never be able to hold one that
    /// disagrees.
    /// </remarks>
    public const string KnowledgeContentFingerprint = "ContentFingerprint";

    /// <summary>The table holding the websites a widget may be embedded on.</summary>
    public const string WebChatAllowedOriginsTable = "web_chat_allowed_origins";

    /// <summary>
    /// Who sent each human reply, and the key that makes sending it twice
    /// harmless.
    /// </summary>
    /// <remarks>
    /// Named here because two schema tests read it: one asserts the unique index
    /// on (conversation_id, client_message_id) exists, and one asserts the
    /// composite foreign key into the workspace membership exists. Both would
    /// otherwise repeat a string literal that a rename could silently orphan.
    /// </remarks>
    public const string HumanRepliesTable = "conversation_human_replies";

    /// <summary>The workspaces' connected WhatsApp numbers.</summary>
    public const string WhatsAppConnectionsTable = "whatsapp_connections";

    /// <summary>
    /// The protected access tokens, kept apart from the connections they belong
    /// to so that no ordinary read touches a credential.
    /// </summary>
    public const string WhatsAppCredentialsTable = "whatsapp_credentials";

    /// <summary>Which conversation each WhatsApp customer is talking in.</summary>
    public const string WhatsAppContactsTable = "whatsapp_contacts";

    /// <summary>The canonical customer directory.</summary>
    public const string CustomersTable = "customers";

    /// <summary>
    /// Which WhatsApp number belongs to which canonical customer.
    /// </summary>
    /// <remarks>
    /// One row per number per workspace, unlike <see cref="WhatsAppContactsTable"/>
    /// which holds one row per conversation.
    /// </remarks>
    public const string WhatsAppCustomerIdentitiesTable = "whatsapp_customer_identities";

    /// <summary>The durable record of every webhook message accepted.</summary>
    public const string InboundWhatsAppMessagesTable = "whatsapp_inbound_messages";

    /// <summary>The durable record of every message owed to a customer.</summary>
    public const string WhatsAppDeliveriesTable = "whatsapp_deliveries";

    /// <summary>
    /// Where each file-backed knowledge document's text came from.
    /// </summary>
    /// <remarks>
    /// Named here because two schema tests read it: one asserts the unique index
    /// that keeps a document to one file, and one asserts the composite foreign
    /// key that makes a cross-workspace source unrepresentable. Both would
    /// otherwise repeat a literal a rename could silently orphan.
    /// </remarks>
    public const string KnowledgeSourcesTable = "knowledge_sources";

    /// <summary>
    /// Real calls to external AI providers, and what they cost.
    /// </summary>
    /// <remarks>
    /// Accounting, not billing: what this platform paid a provider, never what a
    /// customer is charged. It holds no content of any kind.
    /// </remarks>
    /// <summary>What each plan is sold for. Platform-owned, like the plans.</summary>
    public const string BillingPlanOffersTable = "billing_plan_offers";

    /// <summary>Which provider price corresponds to which offer.</summary>
    public const string BillingProviderPriceMappingsTable = "billing_provider_price_mappings";

    /// <summary>A workspace's customer record with a payment provider.</summary>
    public const string WorkspaceBillingCustomersTable = "workspace_billing_customers";

    /// <summary>A workspace's commercial subscription.</summary>
    public const string WorkspaceSubscriptionsTable = "workspace_subscriptions";

    /// <summary>One attempt to buy a subscription.</summary>
    public const string BillingCheckoutIntentsTable = "billing_checkout_intents";

    /// <summary>Verified provider events, stored before they are acted on.</summary>
    public const string BillingEventsTable = "billing_events";

    /// <summary>The platform's plans. Not tenant-scoped.</summary>
    public const string PlansTable = "plans";

    /// <summary>Which plan each workspace is on. One row per workspace.</summary>
    public const string WorkspacePlanAssignmentsTable = "workspace_plan_assignments";

    public const string AiProviderUsagesTable = "ai_provider_usages";

    /// <summary>Report requests and what became of them.</summary>
    public const string ReportExecutionsTable = "report_executions";

    /// <summary>What is known about each generated report file. Never the file.</summary>
    public const string ReportArtifactsTable = "report_artifacts";

    /// <summary>Weekly and monthly report schedules.</summary>
    public const string ReportSchedulesTable = "report_schedules";

    /// <summary>Real Knowledge indexing attempts and how each ended.</summary>
    public const string KnowledgeIndexAttemptsTable = "knowledge_index_attempts";

    /// <summary>The ASP.NET Core Data Protection key ring, shared by every instance.</summary>
    public const string DataProtectionKeysTable = "data_protection_keys";

    /// <summary>Authoritative transitions waiting to become notifications.</summary>
    public const string NotificationOccurrencesTable = "notification_occurrences";

    /// <summary>One member being told about one occurrence.</summary>
    public const string NotificationsTable = "notifications";

    /// <summary>Each member's operational-email choices, per workspace.</summary>
    public const string NotificationPreferencesTable = "notification_preferences";

    /// <summary>Queued operational emails and where each got to.</summary>
    public const string NotificationEmailsTable = "notification_emails";
}
