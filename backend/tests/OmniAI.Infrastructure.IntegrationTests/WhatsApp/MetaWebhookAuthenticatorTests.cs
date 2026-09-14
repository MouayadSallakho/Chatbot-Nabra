using System.Security.Cryptography;
using System.Text;
using OmniAI.Infrastructure.WhatsApp;

namespace OmniAI.Infrastructure.IntegrationTests.WhatsApp;

/// <summary>
/// The check that stands between the public internet and a workspace's inbox.
/// </summary>
/// <remarks>
/// <strong>Every secret here is invented in the test.</strong> No Meta account,
/// no real app secret and no real verify token is involved: the signature is
/// HMAC-SHA256 over the request body, which is something a test can compute
/// exactly as Meta does.
/// </remarks>
public sealed class MetaWebhookAuthenticatorTests
{
    private const string AppSecret = "fake-local-app-secret";

    private const string VerifyToken = "fake-local-verify-token";

    private static readonly byte[] Body = """
        {"object":"whatsapp_business_account","entry":[]}
        """u8.ToArray();

    [Fact]
    public void A_correct_signature_is_accepted()
    {
        Assert.True(Authenticator().IsSignatureValid(Body, Sign(Body)));
    }

    [Fact]
    public void A_missing_signature_is_refused()
    {
        Assert.False(Authenticator().IsSignatureValid(Body, null));
        Assert.False(Authenticator().IsSignatureValid(Body, string.Empty));
    }

    [Theory]
    [InlineData("deadbeef")]
    [InlineData("sha256=")]
    [InlineData("sha256=not-hexadecimal-at-all-but-the-right-length-of-64-chars!!")]
    [InlineData("sha1=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_malformed_signature_is_refused(string header)
    {
        Assert.False(Authenticator().IsSignatureValid(Body, header));
    }

    [Fact]
    public void A_signature_made_with_a_different_secret_is_refused()
    {
        string forged = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("some-other-secret"),
            Body));

        Assert.False(Authenticator().IsSignatureValid(Body, forged));
    }

    [Fact]
    public void Changing_one_byte_of_the_body_invalidates_the_signature()
    {
        string signature = Sign(Body);

        byte[] mutated = [.. Body];
        mutated[^2] ^= 0x01;

        // <strong>This is why the raw bytes are what gets verified.</strong> A
        // route that parsed the payload and re-serialised it before checking
        // would accept this: the two documents mean the same thing and are not
        // the same bytes, and "the same thing" is not what an HMAC covers.
        Assert.True(Authenticator().IsSignatureValid(Body, signature));
        Assert.False(Authenticator().IsSignatureValid(mutated, signature));
    }

    [Fact]
    public void The_verify_token_is_compared_exactly()
    {
        MetaWebhookAuthenticator authenticator = Authenticator();

        Assert.True(authenticator.IsVerifyTokenValid(VerifyToken));
        Assert.False(authenticator.IsVerifyTokenValid(VerifyToken + "x"));
        Assert.False(authenticator.IsVerifyTokenValid(VerifyToken.ToUpperInvariant()));
        Assert.False(authenticator.IsVerifyTokenValid(null));
        Assert.False(authenticator.IsVerifyTokenValid(string.Empty));
    }

    [Fact]
    public void An_unconfigured_deployment_refuses_everything()
    {
        UnconfiguredWebhookAuthenticator authenticator = new("not configured");

        // A deployment that cannot tell a real webhook from an invented one must
        // accept neither. The alternative -- accepting unverified payloads until
        // somebody sets the secret -- would let anybody who guessed a phone
        // number id write into a workspace's inbox.
        Assert.False(authenticator.IsConfigured);
        Assert.False(authenticator.IsSignatureValid(Body, Sign(Body)));
        Assert.False(authenticator.IsVerifyTokenValid(VerifyToken));
    }

    private static string Sign(byte[] body) =>
        "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), body));

    private static MetaWebhookAuthenticator Authenticator() => new(AppSecret, VerifyToken);
}
