using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// Whether a connection is serving.
/// </summary>
/// <remarks>
/// The same three-state shape a website chat endpoint uses, and for the same
/// reason: a connection is created switched off, so a mistyped phone number id
/// is never live the moment it is saved.
/// <para>
/// There is no zero member, so an uninitialised value is detectable.
/// </para>
/// </remarks>
public enum WhatsAppConnectionStatus
{
    /// <summary>Configured but not serving. Webhooks route nowhere.</summary>
    Draft = 1,

    /// <summary>Serving. Inbound messages become conversations.</summary>
    Active = 2,

    /// <summary>Withdrawn. Existing conversations survive; nothing new arrives.</summary>
    Disabled = 3,
}

/// <summary>
/// One workspace's WhatsApp Cloud API connection, bound to one AI agent.
/// </summary>
/// <remarks>
/// <strong>The access token is not here.</strong> It is a secret, and this
/// aggregate holds only the fact that one has been configured. Storing the
/// ciphertext on the aggregate would mean every read of a connection — a list
/// screen, a status check, a webhook routing lookup — carried a credential
/// through memory it has no use for. The protected value lives in its own row,
/// written and read by the one use case that needs it.
/// <para>
/// <strong>One phone number id belongs to one connection, deployment-wide.</strong>
/// The phone number id is the only field an inbound webhook can be routed by, so
/// two connections claiming it would make routing ambiguous — and ambiguous here
/// means one workspace's customers arriving in another workspace's inbox. The
/// database enforces it with a unique index; this type cannot, because an
/// aggregate cannot see its siblings.
/// </para>
/// </remarks>
public sealed class WhatsAppConnection
{
    private WhatsAppConnection(
        WhatsAppConnectionId id,
        TenantId tenantId,
        AiAgentId aiAgentId,
        WabaId wabaId,
        PhoneNumberId phoneNumberId,
        string? displayPhoneNumber,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        AiAgentId = aiAgentId;
        WabaId = wabaId;
        PhoneNumberId = phoneNumberId;
        DisplayPhoneNumber = displayPhoneNumber;
        Status = WhatsAppConnectionStatus.Draft;
        HasCredential = false;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Identity. Fixed for the lifetime of the connection.</summary>
    public WhatsAppConnectionId Id { get; }

    /// <summary>
    /// The owning workspace. No setter of any kind: a connection belongs to one
    /// workspace for its whole life, and that is the platform's isolation
    /// boundary.
    /// </summary>
    public TenantId TenantId { get; }

    /// <summary>The agent that answers on this number.</summary>
    public AiAgentId AiAgentId { get; private set; }

    /// <summary>The WhatsApp Business Account this number belongs to.</summary>
    public WabaId WabaId { get; private set; }

    /// <summary>The number messages are sent from and received on.</summary>
    public PhoneNumberId PhoneNumberId { get; private set; }

    /// <summary>
    /// The human-readable number, purely so an administrator can recognise which
    /// of their numbers this is.
    /// </summary>
    /// <remarks>
    /// Optional, and never used for routing or sending. It is the business's own
    /// published number rather than a customer's, so it is not personal data in
    /// the way an inbound sender is — but it is still stored only because a
    /// screen showing four identical rows of digits would be unusable.
    /// </remarks>
    public string? DisplayPhoneNumber { get; private set; }

    /// <summary>Whether this connection is serving.</summary>
    public WhatsAppConnectionStatus Status { get; private set; }

    /// <summary>
    /// Whether an access token has been configured.
    /// </summary>
    /// <remarks>
    /// A boolean, deliberately. It is the only thing about the credential that
    /// any read path is allowed to learn, and it is what the admin screen shows:
    /// configured, or not. There is no accessor that could return the token,
    /// because this type never holds it.
    /// </remarks>
    public bool HasCredential { get; private set; }

    /// <summary>When the connection was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it was last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether inbound messages on this number may start work.</summary>
    /// <remarks>
    /// Active <em>and</em> credentialed. A connection with no token can receive a
    /// webhook but could never answer it, and starting a conversation nobody can
    /// reply to would leave a customer waiting on silence.
    /// </remarks>
    public bool IsServing => Status == WhatsAppConnectionStatus.Active && HasCredential;

    /// <summary>Creates a connection. It begins as a draft.</summary>
    /// <exception cref="DomainValidationException">
    /// A reference is empty, or the instant is not a real point in time.
    /// </exception>
    public static WhatsAppConnection Create(
        TenantId tenantId,
        AiAgentId aiAgentId,
        WabaId wabaId,
        PhoneNumberId phoneNumberId,
        string? displayPhoneNumber,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A WhatsApp connection must belong to a workspace.");
        }

        if (aiAgentId.IsEmpty)
        {
            throw new DomainValidationException("A WhatsApp connection must name an AI agent.");
        }

        return new WhatsAppConnection(
            WhatsAppConnectionId.New(),
            tenantId,
            aiAgentId,
            wabaId,
            phoneNumberId,
            NormaliseDisplayNumber(displayPhoneNumber),
            RequireInstant(createdAt));
    }

    /// <summary>Rehydrates a stored connection. Persistence only.</summary>
    public static WhatsAppConnection FromStored(
        WhatsAppConnectionId id,
        TenantId tenantId,
        AiAgentId aiAgentId,
        WabaId wabaId,
        PhoneNumberId phoneNumberId,
        string? displayPhoneNumber,
        WhatsAppConnectionStatus status,
        bool hasCredential,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, tenantId, aiAgentId, wabaId, phoneNumberId, displayPhoneNumber, createdAt)
        {
            Status = status,
            HasCredential = hasCredential,
            UpdatedAt = updatedAt,
        };

    /// <summary>Changes which agent answers, and which number this is.</summary>
    /// <remarks>
    /// The phone number id may be changed while the connection is not serving.
    /// Changing it on a live connection would silently redirect a number's
    /// traffic, so callers activate deliberately after editing rather than
    /// editing underneath live traffic.
    /// </remarks>
    /// <exception cref="DomainStateException">The connection is active.</exception>
    public void Reconfigure(
        AiAgentId aiAgentId,
        WabaId wabaId,
        PhoneNumberId phoneNumberId,
        string? displayPhoneNumber,
        DateTimeOffset changedAt)
    {
        if (Status == WhatsAppConnectionStatus.Active)
        {
            throw new DomainStateException(
                "Take this WhatsApp connection out of service before changing which "
                + "number or agent it uses.");
        }

        if (aiAgentId.IsEmpty)
        {
            throw new DomainValidationException("A WhatsApp connection must name an AI agent.");
        }

        AiAgentId = aiAgentId;
        WabaId = wabaId;
        PhoneNumberId = phoneNumberId;
        DisplayPhoneNumber = NormaliseDisplayNumber(displayPhoneNumber);

        Touch(changedAt);
    }

    /// <summary>
    /// Records that an access token has been stored for this connection.
    /// </summary>
    /// <remarks>
    /// The token itself goes somewhere else; this only flips the indicator. It
    /// is a separate operation from <see cref="Reconfigure"/> because replacing
    /// a credential is write-only and must not require re-sending every other
    /// field — a form that made an administrator retype a phone number to rotate
    /// a token would get the phone number wrong eventually.
    /// </remarks>
    public void RecordCredentialStored(DateTimeOffset storedAt)
    {
        HasCredential = true;

        Touch(storedAt);
    }

    /// <summary>Puts the connection into service.</summary>
    /// <exception cref="DomainStateException">
    /// No access token has been configured.
    /// </exception>
    public void Activate(DateTimeOffset activatedAt)
    {
        if (!HasCredential)
        {
            throw new DomainStateException(
                "Add an access token before putting this WhatsApp connection into service. "
                + "Without one it could receive messages and never answer them.");
        }

        if (Status == WhatsAppConnectionStatus.Active)
        {
            return;
        }

        Status = WhatsAppConnectionStatus.Active;

        Touch(activatedAt);
    }

    /// <summary>
    /// Takes the connection out of service. Idempotent.
    /// </summary>
    /// <remarks>
    /// Existing conversations are untouched: the customer really did say those
    /// things, and an administrator still needs to read them. What stops is new
    /// inbound work and new outbound sending.
    /// </remarks>
    public void Disable(DateTimeOffset disabledAt)
    {
        if (Status == WhatsAppConnectionStatus.Disabled)
        {
            return;
        }

        Status = WhatsAppConnectionStatus.Disabled;

        Touch(disabledAt);
    }

    private void Touch(DateTimeOffset instant)
    {
        DateTimeOffset moment = RequireInstant(instant);

        if (moment > UpdatedAt)
        {
            UpdatedAt = moment;
        }
    }

    /// <summary>
    /// Keeps a display number recognisable without pretending to validate it.
    /// </summary>
    /// <remarks>
    /// Deliberately not parsed into a phone-number type. This platform does not
    /// dial it, format it or reason about its country; it shows it back to the
    /// person who typed it. A parser here would reject valid numbers from places
    /// its rules did not anticipate, for no benefit at all.
    /// </remarks>
    private static string? NormaliseDisplayNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length > 32
            ? throw new DomainValidationException(
                "A display phone number must be 32 characters or fewer.")
            : trimmed;
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant)
    {
        if (instant == default)
        {
            throw new DomainValidationException("The instant must be a real point in time.");
        }

        return instant.ToUniversalTime();
    }
}
