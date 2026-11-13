using System.Globalization;

namespace OmniAI.Domain.Notifications;

/// <summary>One notification, addressed to one member.</summary>
public readonly record struct NotificationId
{
    private NotificationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationId New() => new(Guid.CreateVersion7());

    public static NotificationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Notification id must not be an empty GUID.")
            {
                Code = "notifications.id.invalid",
            };
        }

        return new NotificationId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>One authoritative transition that members are to be told about.</summary>
public readonly record struct NotificationOccurrenceId
{
    private NotificationOccurrenceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationOccurrenceId New() => new(Guid.CreateVersion7());

    public static NotificationOccurrenceId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Notification occurrence id must not be an empty GUID.")
            {
                Code = "notifications.occurrence.invalid",
            };
        }

        return new NotificationOccurrenceId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>One queued operational email.</summary>
public readonly record struct NotificationEmailId
{
    private NotificationEmailId(Guid value) => Value = value;

    public Guid Value { get; }

    public static NotificationEmailId New() => new(Guid.CreateVersion7());

    public static NotificationEmailId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Notification email id must not be an empty GUID.")
            {
                Code = "notifications.email_id.invalid",
            };
        }

        return new NotificationEmailId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
