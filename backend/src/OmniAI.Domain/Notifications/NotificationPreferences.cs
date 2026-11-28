using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Notifications;

/// <summary>
/// Which operational emails one member wants, in one workspace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Email is off until the member turns it on.</strong> The platform has
/// no recorded consent to email anybody, so every category starts off. In-app
/// notifications are not a preference: they are always kept.
/// </para>
/// <para>
/// <strong>The member's own, and nobody else's.</strong> There is no
/// administrator override. A workspace can decide who is <em>told</em>, through
/// permissions; only the member decides what reaches their inbox.
/// </para>
/// </remarks>
public sealed class NotificationPreferences
{
    private NotificationPreferences(
        TenantId tenantId,
        UserId userId,
        bool emailReports,
        bool emailUsage,
        bool emailBilling,
        bool emailKnowledge,
        bool emailChannelHealth,
        DateTimeOffset updatedAt)
    {
        TenantId = tenantId;
        UserId = userId;
        EmailReports = emailReports;
        EmailUsage = emailUsage;
        EmailBilling = emailBilling;
        EmailKnowledge = emailKnowledge;
        EmailChannelHealth = emailChannelHealth;
        UpdatedAt = updatedAt;
    }

    public TenantId TenantId { get; }

    public UserId UserId { get; }

    public bool EmailReports { get; private set; }

    public bool EmailUsage { get; private set; }

    public bool EmailBilling { get; private set; }

    public bool EmailKnowledge { get; private set; }

    public bool EmailChannelHealth { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>What a member has before they have chosen anything: no email at all.</summary>
    public static NotificationPreferences Default(TenantId tenantId, UserId userId, DateTimeOffset now)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("Preferences belong to a workspace.")
            {
                Code = "notifications.tenant.required",
            };
        }

        return new NotificationPreferences(tenantId, userId, false, false, false, false, false, now.ToUniversalTime());
    }

    public bool EmailsFor(NotificationCategory category) => category switch
    {
        NotificationCategory.Reports => EmailReports,
        NotificationCategory.Usage => EmailUsage,
        NotificationCategory.Billing => EmailBilling,
        NotificationCategory.Knowledge => EmailKnowledge,
        NotificationCategory.Inbox => false,
        NotificationCategory.ChannelHealth => EmailChannelHealth,
        _ => false,
    };

    public void Choose(
        bool reports,
        bool usage,
        bool billing,
        bool knowledge,
        bool channelHealth,
        DateTimeOffset now)
    {
        EmailReports = reports;
        EmailUsage = usage;
        EmailBilling = billing;
        EmailKnowledge = knowledge;
        EmailChannelHealth = channelHealth;
        UpdatedAt = now.ToUniversalTime();
    }
}
