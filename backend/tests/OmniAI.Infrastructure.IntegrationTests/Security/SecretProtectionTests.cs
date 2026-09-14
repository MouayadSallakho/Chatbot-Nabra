using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Security;
using OmniAI.Infrastructure.Security;

namespace OmniAI.Infrastructure.IntegrationTests.Security;

/// <summary>
/// What the credential protector guarantees, and what it refuses.
/// </summary>
/// <remarks>
/// <strong>The threat this answers is a copy of the database.</strong> A backup,
/// a replica, a dump in a ticket. Every property below is about what somebody
/// holding those bytes can do without also holding the master key, and the answer
/// is meant to be nothing.
/// <para>
/// No key here is a real one. They are random bytes generated in the test.
/// </para>
/// </remarks>
public sealed class SecretProtectionTests
{
    private const string Token = "fake-local-test-token-not-a-real-credential";

    private static readonly Guid Workspace = Guid.CreateVersion7();

    private static readonly Guid Connection = Guid.CreateVersion7();

    [Fact]
    public void A_protected_secret_comes_back_when_the_context_matches()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        Assert.Equal(Token, protector.Unprotect(sealed_, Context()));
    }

    [Fact]
    public void The_plaintext_never_appears_in_the_stored_bytes()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        // The claim a backup makes true or false. Searching the ciphertext for
        // the token is exactly what somebody with a dump would try first.
        Assert.DoesNotContain(
            Encoding.UTF8.GetString(sealed_),
            Token,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            Convert.ToHexString(sealed_),
            Convert.ToHexString(Encoding.UTF8.GetBytes(Token)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_same_secret_protected_twice_produces_different_bytes()
    {
        AesGcmSecretProtector protector = new(Key());

        // A fresh random nonce each time. Without it, two workspaces using the
        // same token would be visibly using the same token, and a row could be
        // recognised without being decrypted.
        Assert.NotEqual(
            Convert.ToHexString(protector.Protect(Token, Context())),
            Convert.ToHexString(protector.Protect(Token, Context())));
    }

    [Fact]
    public void A_different_key_cannot_read_it()
    {
        byte[] sealed_ = new AesGcmSecretProtector(Key()).Protect(Token, Context());

        Assert.Null(new AesGcmSecretProtector(Key()).Unprotect(sealed_, Context()));
    }

    [Fact]
    public void A_tampered_byte_makes_it_unreadable()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        sealed_[^1] ^= 0xFF;

        // Authenticated encryption: a modified ciphertext fails the tag rather
        // than decrypting to something else. An attacker with write access to the
        // database cannot steer what the platform sends.
        Assert.Null(protector.Unprotect(sealed_, Context()));
    }

    [Fact]
    public void Ciphertext_moved_to_another_workspace_decrypts_to_nothing()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        // <strong>The associated data doing its job.</strong> Somebody with
        // database write access copying one workspace's credential row into
        // another's would otherwise have that workspace sending as them.
        Assert.Null(protector.Unprotect(
            sealed_,
            new SecretContext(SecretPurposes.WhatsAppAccessToken, Guid.CreateVersion7(), Connection)));
    }

    [Fact]
    public void Ciphertext_moved_to_another_connection_decrypts_to_nothing()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        Assert.Null(protector.Unprotect(
            sealed_,
            new SecretContext(
                SecretPurposes.WhatsAppAccessToken,
                Workspace,
                Guid.CreateVersion7())));
    }

    [Fact]
    public void Ciphertext_read_for_another_purpose_decrypts_to_nothing()
    {
        AesGcmSecretProtector protector = new(Key());

        byte[] sealed_ = protector.Protect(Token, Context());

        // A future secret of a different kind cannot be unsealed by code that
        // asks for the wrong one, even with the right key and the right row.
        Assert.Null(protector.Unprotect(
            sealed_,
            new SecretContext("something.else", Workspace, Connection)));
    }

    [Fact]
    public void Truncated_or_empty_input_is_refused_rather_than_crashing()
    {
        AesGcmSecretProtector protector = new(Key());

        Assert.Null(protector.Unprotect(null, Context()));
        Assert.Null(protector.Unprotect([], Context()));
        Assert.Null(protector.Unprotect([1, 2, 3], Context()));
    }

    [Fact]
    public void Outside_development_a_missing_key_means_no_credential_can_be_stored()
    {
        ServiceCollection services = new();

        services.AddOmniAiSecretProtection(
            new ConfigurationBuilder().Build(),
            allowEphemeralKey: false);

        ISecretProtector protector = services
            .BuildServiceProvider()
            .GetRequiredService<ISecretProtector>();

        // <strong>It fails, rather than storing something it cannot protect.</strong>
        // A deployment without a master key that silently wrote plaintext would be
        // the worst outcome available.
        Assert.Throws<SecretProtectionUnavailableException>(
            () => protector.Protect(Token, Context()));

        Assert.Null(protector.Unprotect([1, 2, 3], Context()));
    }

    [Fact]
    public void A_development_deployment_may_use_a_key_that_dies_with_the_process()
    {
        ServiceCollection services = new();

        services.AddOmniAiSecretProtection(
            new ConfigurationBuilder().Build(),
            allowEphemeralKey: true);

        ISecretProtector protector = services
            .BuildServiceProvider()
            .GetRequiredService<ISecretProtector>();

        // Usable on a laptop without a key ceremony, and worthless for anything
        // else: a restart makes every stored credential unreadable, which is
        // exactly the property that stops this reaching production by accident.
        Assert.Equal(Token, protector.Unprotect(protector.Protect(Token, Context()), Context()));
    }

    [Fact]
    public void A_configured_key_must_be_exactly_the_size_the_cipher_needs()
    {
        ServiceCollection services = new();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SecretProtectionSetup.MasterKeyVariable] = Convert.ToBase64String(new byte[16]),
            })
            .Build();

        // A short key is a configuration mistake that must stop the deployment,
        // not one that quietly halves the strength of every credential.
        Assert.Throws<InvalidOperationException>(
            () => services.AddOmniAiSecretProtection(configuration, allowEphemeralKey: false));
    }

    private static SecretContext Context() =>
        new(SecretPurposes.WhatsAppAccessToken, Workspace, Connection);

    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);
}
