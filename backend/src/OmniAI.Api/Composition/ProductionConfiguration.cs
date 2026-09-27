namespace OmniAI.Api.Composition;

/// <summary>
/// The settings a real deployment cannot run correctly without, checked before
/// anything is built (Phase 35).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fail at startup, not at the first unlucky request.</strong> Each of
/// these, when missing, was already refused safely somewhere — but only when the
/// affected feature was first used, and one of them silently: without the master
/// key the Data Protection key ring is written to the database <em>unencrypted</em>.
/// A deployment that starts and passes readiness while unable to protect its own
/// sessions, accept a public chat message or create a workspace is worse than one
/// that refuses to start and says why.
/// </para>
/// <para>
/// <strong>Presence only, and names only.</strong> The format of each value is
/// validated where it is used (a 32-byte base64 master key, a 32-character network
/// key secret, an absolute report path outside the application). The message
/// lists the missing keys and never a value.
/// </para>
/// <para>
/// Optional integrations are not here on purpose: without OpenAI, Stripe, Meta or
/// SMTP credentials those features refuse truthfully and the rest of the product
/// works, which is a legitimate deployment.
/// </para>
/// </remarks>
internal static class ProductionConfiguration
{
    /// <summary>Required outside Development, with what each one protects.</summary>
    private static readonly (string Key, string Why)[] Required =
    [
        ("ConnectionStrings:OmniAiDatabase", "the database (connect as the runtime role, not the owner)"),
        ("ConnectionStrings:OmniAiProtectionRedis", "public chat protection, realtime tickets and the multi-instance SignalR backplane"),
        ("OMNIAI_SECRET_MASTER_KEY", "encryption of the Data Protection key ring and stored channel credentials"),
        ("PUBLIC_CHAT_NETWORK_KEY_SECRET", "the keyed network identity public chat limits are counted by"),
        ("Plans:DefaultPlanCode", "the plan a new workspace starts on"),
        ("Reports:Storage:RootPath", "the persistent report artifact volume"),
    ];

    /// <exception cref="InvalidOperationException">
    /// Outside Development, one or more required settings are missing or blank.
    /// </exception>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            return;
        }

        string[] missing =
        [
            .. Required
                .Where(setting => string.IsNullOrWhiteSpace(configuration[setting.Key]))
                .Select(setting => $"{setting.Key} ({setting.Why})"),
        ];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"This {environment.EnvironmentName} deployment is missing required settings: "
                + string.Join("; ", missing)
                + ". Supply them through the environment or a secret store; see "
                + "docs/operations/production-deployment.md.");
        }
    }
}
