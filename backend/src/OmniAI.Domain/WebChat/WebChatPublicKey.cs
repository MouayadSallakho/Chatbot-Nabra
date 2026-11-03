namespace OmniAI.Domain.WebChat;

/// <summary>
/// The opaque key that appears in a hosted chat page's address.
/// </summary>
/// <remarks>
/// <strong>This is not a secret, and nothing may be built as though it were.</strong>
/// It is printed in a URL, shared in emails, pasted into chat clients and, once a
/// real website embeds a chat page, visible to every visitor of that site. Any
/// design that relied on it staying private would be relying on something the
/// product actively prevents.
/// <para>
/// <strong>What it is instead: an unguessable name.</strong> Holding it lets
/// somebody reach one workspace's public chat product surface — read the agent's
/// display name, start a conversation, and talk to that agent. It carries no
/// workspace identity, no permission and no session. Every admin capability is
/// resolved from an authenticated user's membership and permissions, and no code
/// path anywhere accepts this value as evidence of either.
/// </para>
/// <para>
/// <strong>Why it is random rather than sequential.</strong> Being public is not
/// the same as being enumerable. A sequential or guessable key would let anybody
/// walk the whole platform's chat endpoints, spending every workspace's provider
/// budget and harvesting agent display names. 128 bits from a cryptographic RNG
/// makes enumeration useless without making the value a credential.
/// </para>
/// <para>
/// <strong>Stored in the clear, deliberately.</strong> The invitation token in
/// <c>Domain.Invitations</c> is hashed because it is a bearer credential and the
/// database must not hold working copies of it. This value is the opposite: it is
/// meant to be published, and it is looked up by equality on every public
/// request. Hashing it would buy nothing and would only make the lookup an index
/// on a digest of something already public. The two are not the same kind of
/// value and are deliberately not modelled the same way.
/// </para>
/// </remarks>
public readonly record struct WebChatPublicKey
{
    /// <summary>
    /// The exact length of the encoded form.
    /// </summary>
    /// <remarks>
    /// 16 bytes is 22 base64url characters without padding. Fixed, so anything of
    /// another length is rejected before it can reach a database query — which
    /// keeps a malformed public URL from becoming a parameterised scan.
    /// </remarks>
    public const int Length = 22;

    private WebChatPublicKey(string value) => Value = value;

    /// <summary>The encoded key.</summary>
    public string Value { get; }

    /// <summary>Whether this is the default value rather than a real key.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Reads a key supplied from outside — a public URL, or a stored row.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// It is missing, the wrong length, or contains anything outside the
    /// base64url alphabet.
    /// </exception>
    public static WebChatPublicKey Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != Length)
        {
            throw new DomainValidationException(
                $"A web chat public key must be exactly {Length} characters.")
            {
                Code = "website-chat.public_key.invalid",
            };
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                throw new DomainValidationException(
                    "A web chat public key must contain only base64url characters.")
                {
                    Code = "website-chat.public_key.invalid",
                };
            }
        }

        return new WebChatPublicKey(value);
    }

    /// <summary>
    /// Reads a key without throwing, for an untrusted value straight off a URL.
    /// </summary>
    /// <remarks>
    /// The public route is reached by anybody with a browser, and a typo in an
    /// address is not an exceptional condition worth an exception and a stack
    /// trace. The caller turns a <see langword="false"/> into the same not-found
    /// answer an unknown key gets, so a malformed key and an unknown one are
    /// indistinguishable.
    /// </remarks>
    public static bool TryCreate(string? value, out WebChatPublicKey key)
    {
        try
        {
            key = Create(value);

            return true;
        }
        catch (DomainValidationException)
        {
            key = default;

            return false;
        }
    }

    public override string ToString() => Value;
}
