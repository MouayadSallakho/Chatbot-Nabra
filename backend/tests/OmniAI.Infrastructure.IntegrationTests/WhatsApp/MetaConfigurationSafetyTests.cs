using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.WhatsApp;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.WhatsApp;

namespace OmniAI.Infrastructure.IntegrationTests.WhatsApp;

/// <summary>
/// The regression this suite exists to prevent is somebody committing live Meta
/// access.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Composition first, file scanning second.</strong> Most of this asserts
/// what the container actually builds when configuration is absent, because that
/// is the behaviour a credential would have to defeat. The two file scans are
/// narrow and concrete — they look for this repository's own variable names in
/// files this repository commits — rather than hunting for anything that
/// resembles a secret, which is a test that fails on prose and passes on real
/// mistakes.
/// </para>
/// <para>
/// Nothing here reads a real environment variable, and no value in this file is a
/// credential.
/// </para>
/// </remarks>
public sealed class MetaConfigurationSafetyTests
{
    [Fact]
    public void With_no_secrets_the_webhook_refuses_everything()
    {
        // There is no default app secret and no default verify token to fall back
        // on. A deployment that cannot tell a real delivery from an invented one
        // must accept neither -- anybody who learned a phone number id could
        // otherwise put words into a workspace's inbox.
        using ServiceProvider provider = Build();

        IWhatsAppWebhookAuthenticator authenticator =
            provider.GetRequiredService<IWhatsAppWebhookAuthenticator>();

        Assert.False(authenticator.IsConfigured);
        Assert.False(authenticator.IsVerifyTokenValid("anything"));
        Assert.False(authenticator.IsSignatureValid([1, 2, 3], "sha256=00"));
    }

    [Theory]
    [InlineData(null, "verify")]
    [InlineData("secret", null)]
    [InlineData("", "verify")]
    [InlineData("secret", "   ")]
    public void One_secret_without_the_other_is_still_unconfigured(string? appSecret, string? verify)
    {
        // Half-configured is not partly working. Accepting a signature while
        // being unable to answer a subscription, or the reverse, would be a
        // deployment that looks connected and is not.
        using ServiceProvider provider = Build(appSecret, verify);

        Assert.False(provider.GetRequiredService<IWhatsAppWebhookAuthenticator>().IsConfigured);
    }

    [Fact]
    public void Nothing_about_the_transport_options_can_hold_a_credential()
    {
        // The options object is the easy thing to log, serialise into diagnostics
        // or return from a configuration endpoint. No secret may be in it, and
        // none may be added later without this failing.
        string[] properties =
        [
            .. typeof(WhatsAppTransportOptions)
                .GetProperties()
                .Select(property => property.Name),
        ];

        foreach (string suspicious in (string[])["Token", "Secret", "Credential", "Key", "Password"])
        {
            Assert.DoesNotContain(
                properties,
                name => name.Contains(suspicious, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void No_settings_file_in_the_repository_carries_a_meta_secret()
    {
        // The two platform secrets must come from the environment and never from
        // configuration committed to the repository. This walks the real settings
        // files rather than trusting that nobody will add one.
        string[] settings =
        [
            .. Enumerate("appsettings*.json"),
            .. Enumerate("launchSettings.json"),
        ];

        Assert.NotEmpty(settings);

        foreach (string path in settings)
        {
            string content = File.ReadAllText(path);

            Assert.DoesNotContain(
                WhatsAppServiceCollectionExtensions.AppSecretVariable,
                content,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                WhatsAppServiceCollectionExtensions.VerifyTokenVariable,
                content,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void No_committed_configuration_switches_live_meta_on()
    {
        // The opt-in is a decision somebody makes for one session on one machine.
        // Committing it would make it the default for everybody who checks the
        // repository out, which is precisely the accident the guard exists for.
        foreach (string path in (string[])
        [
            .. Enumerate("appsettings*.json"),
            .. Enumerate("launchSettings.json"),
            .. Enumerate("*.yml"),
            .. Enumerate("*.yaml"),
        ])
        {
            Assert.DoesNotContain(
                WhatsAppTransportOptions.LiveProviderOptInVariable,
                File.ReadAllText(path),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_frontend_carries_no_meta_configuration_at_all()
    {
        // The Angular bundle is served to anybody who opens the admin. Nothing
        // about WhatsApp belongs in it: the connection is configured through the
        // API, the credential is write-only, and no Meta identifier or secret is
        // ever handed to the browser.
        DirectoryInfo root = RepositoryRoot();

        string frontend = Path.Combine(root.FullName, "frontend", "src");

        Assert.True(Directory.Exists(frontend));

        foreach (string path in Directory.EnumerateFiles(frontend, "*.ts", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);

            foreach (string forbidden in (string[])
            [
                WhatsAppServiceCollectionExtensions.AppSecretVariable,
                WhatsAppServiceCollectionExtensions.VerifyTokenVariable,
                WhatsAppTransportOptions.LiveProviderOptInVariable,
                WhatsAppTransportOptions.MetaGraphHost,
            ])
            {
                Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // --------------------------------------------------------------- helpers

    private static ServiceProvider Build(string? appSecret = null, string? verifyToken = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    WhatsAppServiceCollectionExtensions.AppSecretVariable,
                    appSecret),
                new KeyValuePair<string, string?>(
                    WhatsAppServiceCollectionExtensions.VerifyTokenVariable,
                    verifyToken),
            ])
            .Build();

        ServiceCollection services = [];

        services.AddLogging();
        services.AddOmniAiWhatsApp(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Committed files of one shape, ignoring build output.</summary>
    private static IEnumerable<string> Enumerate(string pattern) =>
        Directory
            .EnumerateFiles(RepositoryRoot().FullName, pattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// The checkout this test assembly was built from.
    /// </summary>
    /// <remarks>
    /// Found by walking up to the <c>.git</c> directory, the same way the OpenAI
    /// settings scan does. It means these tests need the repository mounted, not
    /// just <c>backend/</c>.
    /// </remarks>
    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory;
    }
}
