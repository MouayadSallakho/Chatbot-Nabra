using System.Text.RegularExpressions;

namespace OmniAI.Infrastructure.IntegrationTests.Deployment;

/// <summary>
/// Phase 35: the security-critical properties of the committed deployment
/// assets, pinned so a later edit cannot quietly undo them.
/// </summary>
/// <remarks>
/// Static checks of the files a deployment runs. The behaviour itself was proven
/// against a running stack (the Phase 35 report, §21); these keep the files from
/// drifting away from what was proven.
/// </remarks>
public sealed partial class DeploymentConfigurationTests
{
    private static readonly string[] ProviderSecrets =
    [
        "OPENAI_API_KEY",
        "STRIPE_SECRET_KEY",
        "STRIPE_WEBHOOK_SECRET",
        "STRIPE_API_BASE",
        "OMNIAI_WHATSAPP_APP_SECRET",
        "OMNIAI_WHATSAPP_VERIFY_TOKEN",
        "Email__Smtp__Password",
        "OMNIAI_SMTP_PASSWORD",
    ];

    /// <remarks>
    /// Compose resolves <c>${VAR}</c> from the calling shell before its env
    /// file. A provider key interpolated this way reached Production containers
    /// from a developer's environment in the Phase 35 drill; provider
    /// credentials may only arrive through the providers file.
    /// </remarks>
    [Fact]
    public void No_provider_credential_is_interpolated_from_the_deploying_shell()
    {
        string compose = Read("deploy", "compose.production.yml");

        foreach (string secret in ProviderSecrets)
        {
            Assert.DoesNotContain("${" + secret, compose, StringComparison.Ordinal);
            Assert.DoesNotMatch($@"(?m)^\s+{Regex.Escape(secret)}\s*:", compose);
        }

        Assert.Contains("env_file:", compose, StringComparison.Ordinal);
        Assert.Contains("${OMNIAI_PROVIDERS_FILE:-./providers.env}", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void Committed_provider_and_environment_templates_hold_placeholders_only()
    {
        foreach (string template in new[] { "providers.env.example", ".env.example" })
        {
            foreach (string line in Read("deploy", template).Split('\n'))
            {
                Match assignment = Assignment().Match(line);

                if (!assignment.Success)
                {
                    continue;
                }

                string key = assignment.Groups["key"].Value;
                string value = assignment.Groups["value"].Value.Trim();

                if (key.Contains("KEY", StringComparison.Ordinal)
                    || key.Contains("SECRET", StringComparison.Ordinal)
                    || key.Contains("PASSWORD", StringComparison.Ordinal)
                    || key.Contains("TOKEN", StringComparison.Ordinal))
                {
                    Assert.True(
                        value.Length == 0 || value.StartsWith("CHANGE_ME", StringComparison.Ordinal),
                        $"{template}: {key} must be empty or a CHANGE_ME placeholder.");
                }
            }
        }
    }

    [Fact]
    public void Real_environment_files_are_ignored_and_kept_out_of_every_build_context()
    {
        string gitignore = Read(".gitignore");
        string dockerignore = Read(".dockerignore");

        Assert.Contains("*.env", gitignore, StringComparison.Ordinal);
        Assert.Contains("**/*.env", dockerignore, StringComparison.Ordinal);
        Assert.Contains("**/.env*", dockerignore, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Liveness is not readiness: routing must follow /health/ready, and an
    /// instance must never be taken out of service because a dependency failed.
    /// </remarks>
    [Fact]
    public void Edge_routing_follows_readiness_and_never_liveness()
    {
        string watcher = Read("deploy", "edge", "readiness-watcher.sh");
        string nginx = Read("deploy", "edge", "nginx.conf");

        Assert.Contains("/health/ready", watcher, StringComparison.Ordinal);
        Assert.DoesNotContain("wget -q -T \"$TIMEOUT\" -O /dev/null \"http://$s/health/live", watcher, StringComparison.Ordinal);
        Assert.Contains("include /tmp/omniai/upstreams.conf;", nginx, StringComparison.Ordinal);
        Assert.Contains("never take the whole site down", watcher, StringComparison.Ordinal);
        Assert.Contains("edge_drain \"$service\" on", Read("deploy", "scripts", "rolling-deploy.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_runtime_role_can_only_read_the_migration_history_and_the_apis_wait_for_that()
    {
        string compose = Read("deploy", "compose.production.yml");

        Assert.Contains(
            "REVOKE ALL ON \"__EFMigrationsHistory\" FROM omniai_app; GRANT SELECT ON \"__EFMigrationsHistory\" TO omniai_app;",
            compose,
            StringComparison.Ordinal);
        Assert.Matches(@"depends_on:\s+migrator-privileges:\s+condition: service_completed_successfully", compose);
        Assert.Contains("run --rm migrator-privileges", Read("deploy", "scripts", "rolling-deploy.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_development_live_switch_is_set_by_the_deployment()
    {
        foreach (string file in new[] { "compose.production.yml", ".env.example", "providers.env.example" })
        {
            Assert.DoesNotContain("OMNIAI_LIVE_", Read("deploy", file), StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$")]
    private static partial Regex Assignment();

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repository().FullName, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static DirectoryInfo Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
