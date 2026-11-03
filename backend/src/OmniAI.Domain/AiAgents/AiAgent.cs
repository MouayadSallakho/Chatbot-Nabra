using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.AiAgents;

/// <summary>
/// A tenant-owned configuration describing an automated customer-facing
/// assistant: what it is called, what it is for, and whether it is switched on.
/// </summary>
/// <remarks>
/// <b>An <c>AiAgent</c> is not an OpenAI Assistant.</b> It is also not an
/// Anthropic agent, a WhatsApp bot, an OpenAI thread, an LLM session, a vector
/// store or a remote object of any kind. It is what a business means when it says
/// "our sales assistant" -- a decision the tenant made, recorded by this
/// platform, which would still be meaningful if every AI provider on the market
/// were replaced tomorrow. No provider type, model name, sampling parameter,
/// credential, endpoint or external identifier appears here, and none may be
/// added: the whole point of the type is that the business configuration outlives
/// the vendor.
/// <para>
/// The aggregate is deliberately narrow. Knowledge sources, tools, channel
/// assignments, human handoff rules, memory, guardrails and cost controls are
/// each their own concern with their own rules, and each will own its own types
/// rather than accumulate here.
/// </para>
/// <para>
/// State changes go through intentional operations rather than public setters, so
/// an invariant cannot be bypassed by assigning a property.
/// </para>
/// </remarks>
public sealed class AiAgent
{
    private AiAgent(
        AiAgentId id,
        TenantId tenantId,
        AiAgentName name,
        AgentInstructions instructions,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Instructions = instructions;
        Status = AiAgentStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Identity. Fixed for the lifetime of the agent.</summary>
    public AiAgentId Id { get; }

    /// <summary>
    /// The owning tenant. Deliberately has no setter of any kind, not even a
    /// private one, and there is no <c>ChangeTenant</c> operation.
    /// </summary>
    /// <remarks>
    /// An agent belongs to exactly one tenant for its whole life and can never be
    /// moved to another. Tenant ownership is the platform's isolation boundary, so
    /// making it structurally immutable removes a whole class of cross-tenant
    /// leak: there is no code path -- correct, buggy or malicious -- that can
    /// reassign it, because none exists to call. Moving an agent between
    /// businesses would in any case mean handing one tenant's instructions to
    /// another, which is a data transfer with its own rules, not a property
    /// update.
    /// </remarks>
    public TenantId TenantId { get; }

    /// <summary>The business-facing display name.</summary>
    public AiAgentName Name { get; private set; }

    /// <summary>What the agent is for, in the business's own words.</summary>
    public AgentInstructions Instructions { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public AiAgentStatus Status { get; private set; }

    /// <summary>When the agent was created, in UTC. Never changes.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The high-water mark of when this agent last actually changed, in UTC.
    /// </summary>
    /// <remarks>
    /// Starts at the creation instant and never moves backwards. A mutation
    /// supplied with an instant earlier than the current value is still applied --
    /// the Domain records what it is told and does not silently discard an edit --
    /// but leaves this value alone, so a consumer sorting or displaying by
    /// "last changed" is never told the agent changed before it did.
    /// <para>
    /// Same reasoning as the accepted <c>Conversation.LastActivityAt</c> decision:
    /// requiring supplied instants to increase would push a distributed-clock
    /// problem into the wrong layer. This is also <b>not</b> a version token or a
    /// concurrency check -- it cannot be, since two edits can share an instant.
    /// Losing a concurrent update is prevented by the persistence layer's
    /// optimistic concurrency, which is already in place.
    /// </para>
    /// <para>
    /// An operation that changes nothing -- renaming to the current name,
    /// activating an already active agent -- does not advance it, because nothing
    /// about the agent changed.
    /// </para>
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates an agent. It begins <see cref="AiAgentStatus.Draft"/>: existing
    /// and valid, but not yet switched on, because deciding when an agent starts
    /// serving customers is the tenant's, not a side effect of saving it.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="name">The display name.</param>
    /// <param name="instructions">What the agent is for.</param>
    /// <param name="createdAt">The effective creation instant, supplied by the caller.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="instructions"/> is null.</exception>
    /// <exception cref="DomainValidationException">
    /// The tenant id is empty or the instant is not a real point in time.
    /// </exception>
    public static AiAgent Create(
        TenantId tenantId,
        AiAgentName name,
        AgentInstructions instructions,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(instructions);

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("An AI agent must belong to a tenant.")
            {
                Code = "ai-agents.tenant.required",
            };
        }

        return new AiAgent(
            AiAgentId.New(),
            tenantId,
            name,
            instructions,
            RequireInstant(createdAt, "creation instant"));
    }

    /// <summary>
    /// Changes the display name. Renaming to the name it already has changes
    /// nothing, including <see cref="UpdatedAt"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Rename(AiAgentName name, DateTimeOffset renamedAt)
    {
        ArgumentNullException.ThrowIfNull(name);

        DateTimeOffset instant = RequireInstant(renamedAt, "rename instant");

        if (Name == name)
        {
            return;
        }

        Name = name;
        RecordUpdate(instant);
    }

    /// <summary>
    /// Replaces the agent's instructions. Allowed in every status, including
    /// <see cref="AiAgentStatus.Active"/>.
    /// </summary>
    /// <remarks>
    /// Editing a live agent's instructions is the normal way a business corrects
    /// its behaviour, and forcing it to be disabled first would mean taking the
    /// agent off the air to fix a typo. Because the previous text is replaced
    /// rather than kept, there is no history of what it used to say -- version
    /// history is recorded as deferred, not as something this type quietly does.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="instructions"/> is null.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void UpdateInstructions(AgentInstructions instructions, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        DateTimeOffset instant = RequireInstant(updatedAt, "update instant");

        if (Instructions == instructions)
        {
            return;
        }

        Instructions = instructions;
        RecordUpdate(instant);
    }

    /// <summary>
    /// Switches the agent on, from <see cref="AiAgentStatus.Draft"/> or
    /// <see cref="AiAgentStatus.Disabled"/>. Idempotent: activating an already
    /// active agent is a no-op.
    /// </summary>
    /// <remarks>
    /// Idempotent because the caller's intent -- "this agent must be on" -- is
    /// already satisfied, and demanding a check-then-act sequence would invite a
    /// race while preventing nothing.
    /// <para>
    /// There is no configuration check here, and that is not an omission. An
    /// agent's Domain configuration is valid by construction: <see cref="Name"/>
    /// and <see cref="Instructions"/> are non-null value objects that validated
    /// themselves, and no operation can leave the aggregate in a state where
    /// either is missing or malformed. A runtime guard would therefore be a check
    /// that cannot fail. When a later phase introduces a requirement that
    /// <em>can</em> be unmet -- a selected model, a connected channel, an
    /// approved tool -- that requirement gets enforced here, and only then does a
    /// guard mean something.
    /// </para>
    /// <para>
    /// Whether an active agent is additionally <em>ready</em> to serve a
    /// particular channel is a separate question: readiness depends on
    /// integrations and credentials that do not exist yet, and it belongs to the
    /// Application layer. Mixing it into this Domain state would make the
    /// aggregate's validity depend on the health of external systems.
    /// </para>
    /// </remarks>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Activate(DateTimeOffset activatedAt)
    {
        DateTimeOffset instant = RequireInstant(activatedAt, "activation instant");

        if (Status == AiAgentStatus.Active)
        {
            return;
        }

        Status = AiAgentStatus.Active;
        RecordUpdate(instant);
    }

    /// <summary>
    /// Switches an active agent off. Idempotent: disabling an already disabled
    /// agent is a no-op.
    /// </summary>
    /// <remarks>
    /// A <see cref="AiAgentStatus.Draft"/> agent cannot be disabled. This is the
    /// one non-idempotent-looking rule in the lifecycle, and it is deliberate:
    /// a draft has never been switched on, so there is nothing to withhold, and
    /// treating the request as a no-op would silently accept a call that made no
    /// sense. Allowing it would also be worse -- there is no path back from
    /// <see cref="AiAgentStatus.Disabled"/> to <see cref="AiAgentStatus.Draft"/>,
    /// so a draft disabled by mistake could only be recovered by activating it,
    /// which is precisely what the tenant was trying to avoid.
    /// </remarks>
    /// <exception cref="DomainStateException">The agent is still a draft.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Disable(DateTimeOffset disabledAt)
    {
        DateTimeOffset instant = RequireInstant(disabledAt, "disable instant");

        if (Status == AiAgentStatus.Draft)
        {
            throw new DomainStateException(
                "Cannot disable an AI agent that is still a draft. A draft has never been " +
                "active, so there is nothing to switch off.")
            {
                Code = "ai-agents.disable.draft",
            };
        }

        if (Status == AiAgentStatus.Disabled)
        {
            return;
        }

        Status = AiAgentStatus.Disabled;
        RecordUpdate(instant);
    }

    /// <summary>
    /// Records that something the agent depends on, but does not itself own, has
    /// been changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This adds no state to the aggregate, and that is the point.</strong>
    /// Which knowledge documents an agent may draw on is a relationship, kept in
    /// its own table rather than accumulated here -- but it is still part of how
    /// this agent behaves, and it is edited on the same screen, against the same
    /// entity tag, as the name and the instructions.
    /// </para>
    /// <para>
    /// <strong>Without it, one editor could silently undo another.</strong> Two
    /// people open the agent at version 7. One changes the instructions and saves;
    /// the other changes the assigned knowledge and saves. If the second write
    /// touched only the join table, it would succeed against a version that no
    /// longer describes the agent it was deciding about -- and the first person
    /// would never learn that the configuration they had just written was
    /// answered from different documents. Moving the version means the second
    /// write is refused with a precondition failure, and the person is asked to
    /// look again.
    /// </para>
    /// <para>
    /// It says only that the agent's wider configuration changed at an instant.
    /// It does not say what changed, and nothing here knows.
    /// </para>
    /// </remarks>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void MarkConfigurationChanged(DateTimeOffset changedAt) =>
        RecordUpdate(RequireInstant(changedAt, "configuration change instant"));

    /// <summary>
    /// Advances the update high-water mark, never regressing it. Called only
    /// after something has actually changed.
    /// </summary>
    private void RecordUpdate(DateTimeOffset instant)
    {
        if (instant > UpdatedAt)
        {
            UpdatedAt = instant;
        }
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant, string what)
    {
        if (instant == default)
        {
            throw new DomainValidationException($"The {what} must be a real point in time.")
            {
                Code = "ai-agents.instant.invalid",
            };
        }

        // Stored in UTC: the instant is what matters, and a single representation
        // keeps comparison and storage unambiguous.
        return instant.ToUniversalTime();
    }
}
