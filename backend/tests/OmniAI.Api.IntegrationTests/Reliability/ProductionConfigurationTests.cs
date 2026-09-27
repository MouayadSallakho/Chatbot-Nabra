using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OmniAI.Api.Composition;

namespace OmniAI.Api.IntegrationTests.Reliability;

/// <summary>
/// Phase 35: outside Development, a deployment missing what it cannot run
/// correctly without refuses to start, and says which keys -- never a value.
/// </summary>
public sealed class ProductionConfigurationTests
{
    private static readonly Dictionary<string, string?> Complete = new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:OmniAiDatabase"] = "Host=db.invalid;Database=omniai",
        ["ConnectionStrings:OmniAiProtectionRedis"] = "redis.invalid:6379",
        ["OMNIAI_SECRET_MASTER_KEY"] = "canary-master-key-value",
        ["PUBLIC_CHAT_NETWORK_KEY_SECRET"] = "canary-network-key-secret-value-000",
        ["Plans:DefaultPlanCode"] = "starter",
        ["Reports:Storage:RootPath"] = "/var/lib/omniai/reports",
    };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_complete_configuration_is_accepted(string environment) =>
        ProductionConfiguration.Validate(Build(Complete), Host(environment));

    [Theory]
    [InlineData("ConnectionStrings:OmniAiDatabase")]
    [InlineData("ConnectionStrings:OmniAiProtectionRedis")]
    [InlineData("OMNIAI_SECRET_MASTER_KEY")]
    [InlineData("PUBLIC_CHAT_NETWORK_KEY_SECRET")]
    [InlineData("Plans:DefaultPlanCode")]
    [InlineData("Reports:Storage:RootPath")]
    public void Each_required_setting_missing_or_blank_refuses_startup_and_is_named(string key)
    {
        foreach (string? value in new[] { null, "", "   " })
        {
            Dictionary<string, string?> settings = new(Complete, StringComparer.Ordinal) { [key] = value };

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => ProductionConfiguration.Validate(Build(settings), Host("Production")));

            Assert.Contains(key, refused.Message, StringComparison.Ordinal);

            // Names only: none of the other, present values leaks into the message.
            foreach (string present in Complete.Values.OfType<string>())
            {
                Assert.DoesNotContain(present, refused.Message, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Everything_missing_is_reported_at_once()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => ProductionConfiguration.Validate(Build([]), Host("Production")));

        foreach (string key in Complete.Keys)
        {
            Assert.Contains(key, refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Development_keeps_its_local_fallbacks() =>
        ProductionConfiguration.Validate(Build([]), Host(Environments.Development));

    private static IConfiguration Build(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Environment Host(string name) => new(name);

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "OmniAI.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
