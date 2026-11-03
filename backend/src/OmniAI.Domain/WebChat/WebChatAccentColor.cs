using System.Globalization;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// The one colour a workspace may choose for its widget.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A six-digit hex colour and nothing else.</strong> Not a CSS colour
/// value: <c>var(--x)</c>, <c>url(...)</c>, <c>red; position:fixed</c> and every
/// other CSS-shaped string is refused here rather than sanitised later. A value
/// that can only be six hex digits cannot carry a payload, whatever any renderer
/// downstream does with it.
/// </para>
/// <para>
/// Three-digit shorthand is expanded and the letters are lowercased, so one colour
/// has one stored form and two workspaces writing <c>#FFF</c> and <c>#ffffff</c>
/// are not storing different things.
/// </para>
/// <para>
/// The readable foreground is derived rather than configured. Letting a workspace
/// pick both would let them pick white on white, and the person who suffers for it
/// is a customer who cannot read the reply.
/// </para>
/// </remarks>
public sealed record WebChatAccentColor
{
    /// <summary>The colour used when a workspace has expressed no preference.</summary>
    public const string DefaultValue = "#2563eb";

    private WebChatAccentColor(string value) => Value = value;

    /// <summary>Canonical <c>#rrggbb</c>, lowercase.</summary>
    public string Value { get; }

    /// <summary>The platform default.</summary>
    public static WebChatAccentColor Default { get; } = new(DefaultValue);

    public static WebChatAccentColor Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("An accent colour is required.")
            {
                Code = "website-chat.accent_color.required",
            };
        }

        string trimmed = value.Trim();

        if (trimmed.Length is not (4 or 7) || trimmed[0] != '#')
        {
            throw new DomainValidationException(
                "An accent colour must be a hex colour such as #2563eb.")
            {
                Code = "website-chat.accent_color.invalid",
            };
        }

        foreach (char character in trimmed.AsSpan(1))
        {
            if (!char.IsAsciiHexDigit(character))
            {
                throw new DomainValidationException(
                    "An accent colour must be a hex colour such as #2563eb.")
                {
                    Code = "website-chat.accent_color.invalid",
                };
            }
        }

        string digits = trimmed[1..].ToLowerInvariant();

        // #abc means #aabbcc. Expanded on the way in so the stored value has one
        // shape and nothing downstream has to know the shorthand exists.
        if (digits.Length == 3)
        {
            digits = string.Create(
                CultureInfo.InvariantCulture,
                $"{digits[0]}{digits[0]}{digits[1]}{digits[1]}{digits[2]}{digits[2]}");
        }

        return new WebChatAccentColor($"#{digits}");
    }

    public override string ToString() => Value;
}
