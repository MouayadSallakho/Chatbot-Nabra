using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Security;

namespace OmniAI.Infrastructure.Security;

/// <summary>
/// Registers the thing that protects tenant secrets at rest.
/// </summary>
public static class SecretProtectionSetup
{
    /// <summary>
    /// Environment variable holding the master key, base64-encoded.
    /// </summary>
    /// <remarks>
    /// <strong>A flat variable, never a settings section.</strong> Everything in
    /// <c>appsettings</c> belongs in source control and in a config map; this is
    /// the one value that must be in neither. Keeping it out of the section makes
    /// that impossible to miss, exactly as <c>OPENAI_API_KEY</c> and
    /// <c>PUBLIC_CHAT_NETWORK_KEY_SECRET</c> already do. A test asserts no
    /// settings file in the repository contains it.
    /// </remarks>
    public const string MasterKeyVariable = "OMNIAI_SECRET_MASTER_KEY";

    /// <summary>
    /// Adds the secret protector, or the refusal that stands in for it.
    /// </summary>
    /// <param name="allowEphemeralKey">
    /// <para>
    /// When <see langword="true"/> and no key is configured, a random per-process
    /// key is generated so a developer who has just cloned the repository can use
    /// the feature without first being told about a variable.
    /// </para>
    /// <para>
    /// <strong>The composition root passes Development here and nothing else.</strong>
    /// An ephemeral key means every token stored in one run is unreadable in the
    /// next — which is a harmless nuisance locally and would be data loss in a
    /// deployment, so outside Development a missing key is a refusal rather than
    /// a fallback.
    /// </para>
    /// </param>
    public static IServiceCollection AddOmniAiSecretProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowEphemeralKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? configured = configuration[MasterKeyVariable];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            services.AddSingleton<ISecretProtector>(
                new AesGcmSecretProtector(ReadKey(configured)));

            return services;
        }

        if (allowEphemeralKey)
        {
            // Random per process, and deliberately not derived from anything
            // stable: a key that survived a restart would be a key somebody
            // could reconstruct, and that is worse than one that obviously does
            // not last.
            services.AddSingleton<ISecretProtector>(
                new AesGcmSecretProtector(
                    RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeyBytes)));

            return services;
        }

        services.AddSingleton<ISecretProtector>(new UnconfiguredSecretProtector(
            "This deployment cannot store channel credentials because no secret "
            + $"master key is configured. Set {MasterKeyVariable} to a base64 "
            + $"{AesGcmSecretProtector.KeyBytes}-byte key."));

        return services;
    }

    /// <summary>
    /// Reads the configured key, refusing anything that is not a real AES-256 key.
    /// </summary>
    /// <remarks>
    /// Checked at startup rather than on first use, so a mistyped key is a
    /// deployment failure an operator sees rather than a runtime failure a
    /// customer discovers. The exception names what is wrong and never contains
    /// the value.
    /// </remarks>
    private static byte[] ReadKey(string configured)
    {
        byte[] key;

        try
        {
            key = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{MasterKeyVariable} must be base64. It is not, so no channel "
                + "credential could be stored or read.");
        }

        return key.Length == AesGcmSecretProtector.KeyBytes
            ? key
            : throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{MasterKeyVariable} must decode to exactly "
                    + $"{AesGcmSecretProtector.KeyBytes} bytes for AES-256; it decoded to "
                    + $"{key.Length}."));
    }
}
