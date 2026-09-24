using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Notifications;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Notifications.Email;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Operational notifications: the stores, the SMTP adapter and the three workers.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    /// <param name="requireLiveEmailOptIn">
    /// True in Development. A non-loopback SMTP host is then withheld unless
    /// <c>OMNIAI_LIVE_EMAIL</c> is exactly <c>1</c>, so a developer's machine never
    /// emails a real inbox by accident. A loopback sink is always allowed, and
    /// Production never needs the opt-in.
    /// </param>
    public static IServiceCollection AddOmniAiNotifications(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireLiveEmailOptIn)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        NotificationOptions options = new();
        configuration.GetSection(NotificationOptions.Section).Bind(options);
        options.Validate();
        services.AddSingleton(options);

        OperationalEmailOptions email = new();
        configuration.GetSection(OperationalEmailOptions.SectionName).Bind(email);
        email.Validate();

        services.AddSingleton(email);
        services.AddSingleton<IOperationalEmailSender>(SenderFor(email, configuration, requireLiveEmailOptIn));

        // Only ever read when a sender is configured, which requires a validated
        // address. The placeholder is never put in an email.
        services.AddSingleton(new OperationalEmailLinks(email.AppBaseUri ?? new Uri("http://localhost/")));

        services.AddScoped<NotificationStore>();
        services.AddScoped<INotificationStore>(provider => provider.GetRequiredService<NotificationStore>());
        services.AddScoped<INotificationRetentionStore>(provider => provider.GetRequiredService<NotificationStore>());

        services.AddScoped<NotificationOccurrenceStore>();
        services.AddScoped<INotificationOccurrenceStore>(provider => provider.GetRequiredService<NotificationOccurrenceStore>());
        services.AddScoped<INotificationOccurrenceRecorder>(provider => provider.GetRequiredService<NotificationOccurrenceStore>());

        services.AddScoped<NotificationDirectory>();
        services.AddScoped<INotificationRecipientDirectory>(provider => provider.GetRequiredService<NotificationDirectory>());
        services.AddScoped<INotificationSources>(provider => provider.GetRequiredService<NotificationDirectory>());

        services.AddScoped<INotificationEmailOutbox, NotificationEmailOutbox>();

        services.AddScoped<NotificationHandlers>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<OperationalEmailDispatcher>();
        services.AddScoped<NotificationRetention>();

        NotificationWorkerOptions workers = new();
        configuration.GetSection(NotificationWorkerOptions.SectionName).Bind(workers);
        workers.Validate();

        services.Configure<NotificationWorkerOptions>(configuration.GetSection(NotificationWorkerOptions.SectionName));

        services.AddHostedService<NotificationDispatchWorker>();
        services.AddHostedService<OperationalEmailWorker>();
        services.AddHostedService<NotificationRetentionWorker>();

        return services;
    }

    /// <summary>
    /// Which sender this deployment gets. Decided once, at startup.
    /// </summary>
    internal static IOperationalEmailSender SenderFor(
        OperationalEmailOptions email,
        IConfiguration configuration,
        bool requireLiveEmailOptIn)
    {
        if (!email.IsConfigured)
        {
            return new UnconfiguredOperationalEmailSender();
        }

        bool withheld = requireLiveEmailOptIn
            && !email.TargetsLoopback
            && !string.Equals(
                configuration[OperationalEmailOptions.LiveEmailOptInVariable],
                OperationalEmailOptions.LiveEmailOptInValue,
                StringComparison.Ordinal);

        return withheld ? new WithheldOperationalEmailSender() : new SmtpOperationalEmailSender(email);
    }
}
