using System.Globalization;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// The identity of a website chat endpoint.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model, so rows written in
/// sequence land in sequence in the index rather than scattering across it.
/// <para>
/// <strong>This is the administrative identity, not the public one.</strong> It
/// appears in the admin routes that read, activate and disable an endpoint, and
/// only somebody who may administer the workspace ever sees it. What a visitor's
/// browser carries is <see cref="WebChatPublicKey"/>, a separate opaque value —
/// so publishing a chat page never publishes the identifier the admin API is
/// addressed by.
/// </para>
/// </remarks>
public readonly record struct WebChatEndpointId
{
    private WebChatEndpointId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static WebChatEndpointId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static WebChatEndpointId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Web chat endpoint id must not be an empty GUID.")
            {
                Code = "website-chat.id.invalid",
            };
        }

        return new WebChatEndpointId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
