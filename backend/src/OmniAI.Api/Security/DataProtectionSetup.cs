using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniAI.Application.Security;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Security;

namespace OmniAI.Api.Security;

/// <summary>
/// One Data Protection key ring for every instance and every restart.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The defect this closes.</strong> The session cookie and the
/// antiforgery pair are protected with Data Protection keys. The framework
/// default keeps those keys on the local disk of each process, so a redeployed
/// API began with a brand-new ring: every open browser's session and antiforgery
/// token became unreadable at once, and a second instance could never read the
/// first one's cookies. Keeping the ring in PostgreSQL makes both survive.
/// </para>
/// <para>
/// <strong>Nothing about CSRF is relaxed.</strong> Tokens are validated exactly as
/// before; they are simply decryptable by the same platform that issued them.
/// </para>
/// </remarks>
internal static class DataProtectionSetup
{
    public const string ApplicationName = "OmniAI";

    public static IServiceCollection AddOmniAiDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Encrypt only under a configured master key. Development without one uses
        // a per-process random key, and a ring encrypted under that would be
        // unreadable after the next restart — the very failure this removes.
        bool encrypt = !string.IsNullOrWhiteSpace(configuration[SecretProtectionSetup.MasterKeyVariable]);

        services.AddDataProtection().SetApplicationName(ApplicationName);

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new PostgresXmlRepository(provider.GetRequiredService<IServiceScopeFactory>());

                if (encrypt)
                {
                    options.XmlEncryptor = new MasterKeyXmlEncryptor(provider.GetRequiredService<ISecretProtector>());
                }
            }));

        return services;
    }
}

/// <summary>The key ring's storage: rows in <c>data_protection_keys</c>.</summary>
internal sealed class PostgresXmlRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using IServiceScope scope = scopes.CreateScope();

        OmniAiDbContext context = scope.ServiceProvider.GetRequiredService<OmniAiDbContext>();

        return
        [
            .. context.DataProtectionKeys
                .AsNoTracking()
                .OrderBy(row => row.Id)
                .Select(row => row.Xml)
                .ToList()
                .Select(XElement.Parse),
        ];
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using IServiceScope scope = scopes.CreateScope();

        OmniAiDbContext context = scope.ServiceProvider.GetRequiredService<OmniAiDbContext>();

        context.DataProtectionKeys.Add(new DataProtectionKeyRow
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });

        context.SaveChanges();
    }
}

/// <summary>Encrypts each key under the platform master key before it is stored.</summary>
internal sealed class MasterKeyXmlEncryptor(ISecretProtector protector) : IXmlEncryptor
{
    /// <summary>Binds the ciphertext to its use, so it cannot be swapped for a channel credential.</summary>
    internal static readonly SecretContext Context = new("dataprotection.key-ring", Guid.Empty, Guid.Empty);

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        byte[] ciphertext = protector.Protect(plaintextElement.ToString(SaveOptions.DisableFormatting), Context);

        return new EncryptedXmlInfo(
            new XElement("omniaiEncryptedKey", Convert.ToBase64String(ciphertext)),
            typeof(MasterKeyXmlDecryptor));
    }
}

/// <summary>Reverses <see cref="MasterKeyXmlEncryptor"/>. Activated by Data Protection with the service provider.</summary>
internal sealed class MasterKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        ISecretProtector protector = services.GetRequiredService<ISecretProtector>();

        string xml = protector.Unprotect(Convert.FromBase64String(encryptedElement.Value), MasterKeyXmlEncryptor.Context)
            ?? throw new InvalidOperationException(
                "A Data Protection key could not be decrypted with the configured master key.");

        return XElement.Parse(xml);
    }
}
