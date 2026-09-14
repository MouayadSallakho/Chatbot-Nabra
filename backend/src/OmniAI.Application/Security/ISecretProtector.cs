namespace OmniAI.Application.Security;

/// <summary>
/// What a protected secret is bound to.
/// </summary>
/// <param name="Purpose">
/// What kind of secret this is — a short constant, not a description. Binding it
/// means ciphertext protected for one purpose cannot be decrypted as another,
/// even by code that has the right key.
/// </param>
/// <param name="TenantId">
/// The workspace the secret belongs to. This is the binding that matters most:
/// it makes a ciphertext lifted from one workspace's row and pasted into
/// another's fail to decrypt rather than quietly work.
/// </param>
/// <param name="ResourceId">
/// The row the secret belongs to, so a ciphertext moved between two connections
/// of the same workspace fails too.
/// </param>
/// <remarks>
/// <strong>This is the associated data, and it is the reason the whole type
/// exists.</strong> Encryption alone stops somebody reading a token out of a
/// database dump. It does nothing about somebody who can <em>write</em> to the
/// database: they could copy a working ciphertext from one row into another, and
/// the platform would decrypt it happily and send another workspace's messages
/// with it. Authenticating the context alongside the ciphertext turns that into
/// a decryption failure.
/// </remarks>
public readonly record struct SecretContext(string Purpose, Guid TenantId, Guid ResourceId);

/// <summary>
/// Protects a secret so it can be stored, and unprotects it so it can be used.
/// </summary>
/// <remarks>
/// <strong>Deliberately small, and deliberately not a key-management API.</strong>
/// There is no rotation here, no key listing and no algorithm choice: the
/// Application knows that a secret goes in and comes back, and nothing else.
/// Everything about how — the cipher, the nonce, the tag, where the master key
/// comes from — is an Infrastructure decision, which is what lets it change
/// without touching a single use case.
/// <para>
/// <strong>Unprotecting answers <see langword="null"/> rather than throwing on a
/// bad ciphertext.</strong> A tampered or foreign value is not an exceptional
/// condition to be reported up the stack with a stack trace; it is an answer,
/// and the caller's job is to refuse to send. Throwing would also make the
/// difference between "wrong key" and "wrong workspace" visible in a log, which
/// is information nobody outside this layer should be given.
/// </para>
/// </remarks>
public interface ISecretProtector
{
    /// <summary>
    /// Protects a secret for one workspace, one resource and one purpose.
    /// </summary>
    /// <returns>
    /// Opaque bytes safe to store. They contain no plaintext and cannot be
    /// unprotected without both the master key and the same context.
    /// </returns>
    byte[] Protect(string secret, SecretContext context);

    /// <summary>
    /// Recovers a secret protected for exactly this context.
    /// </summary>
    /// <returns>
    /// The secret, or <see langword="null"/> when the ciphertext is malformed,
    /// was tampered with, was protected for a different context, or was
    /// protected under a different key.
    /// </returns>
    string? Unprotect(byte[]? protectedSecret, SecretContext context);
}

/// <summary>
/// The purposes this platform protects secrets for.
/// </summary>
/// <remarks>
/// Named constants rather than literals at call sites, so a typo is a build
/// error rather than a value that encrypts fine and never decrypts.
/// </remarks>
public static class SecretPurposes
{
    /// <summary>A workspace's WhatsApp Cloud API access token.</summary>
    public const string WhatsAppAccessToken = "whatsapp.access-token";
}

/// <summary>
/// Raised when a deployment is missing the configuration needed to protect
/// secrets at all.
/// </summary>
/// <remarks>
/// <strong>A startup-shaped failure, surfaced at use rather than at startup.</strong>
/// The platform must still boot without an encryption key — every screen that
/// has nothing to do with WhatsApp keeps working, and an operator gets a
/// message naming what is missing the first time somebody tries to store a
/// token. Refusing to start would take an entire deployment offline over a
/// feature it may not use.
/// </remarks>
public sealed class SecretProtectionUnavailableException(string message) : Exception(message);
