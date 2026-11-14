using System.Globalization;
using System.Text;

namespace OmniAI.Domain.Users;

/// <summary>
/// The name a person's colleagues see beside their email (Phase 39.1, note 7).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One per account, not per workspace.</strong> A person's name is the
/// same in every workspace they belong to, and they edit it once, themselves. It
/// is staff-facing only: a visitor's transcript keeps saying "Support" (owner
/// decision Q5, 2026-11-13).
/// </para>
/// <para>
/// <strong>One line of plain text.</strong> Trimmed, with every run of
/// whitespace inside collapsed to one space; blank means "no name". At most
/// <see cref="MaxLength"/> characters, counted in UTF-16 code units as
/// <c>WebChatGreeting</c> counts them. No control character and no format
/// character, except the two joiners Arabic script and emoji sequences need
/// (U+200C ZWNJ and U+200D ZWJ): an invisible direction mark in a name would
/// reorder whatever is printed after it.
/// </para>
/// </remarks>
public sealed record MemberDisplayName
{
    public const int MaxLength = 80;

    /// <summary>The unit <see cref="MaxLength"/> counts, as the refusal's <c>limitUnit</c> says.</summary>
    public const string LimitUnit = "characters";

    private const char ZeroWidthNonJoiner = '‌';

    private const char ZeroWidthJoiner = '‍';

    private MemberDisplayName(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Reads a name, treating blank as "no name at all".
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// Longer than <see cref="MaxLength"/> after collapsing, or carrying a control or
    /// format character. It is never truncated.
    /// </exception>
    public static MemberDisplayName? CreateOrNone(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string collapsed = Collapse(value);

        if (collapsed.Length == 0)
        {
            return null;
        }

        if (collapsed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A display name must be at most {MaxLength} characters; the supplied name has "
                + $"{collapsed.Length}. It is not truncated.")
            {
                Code = "account.display_name.too_long",
                Limit = MaxLength,
                LimitUnit = LimitUnit,
            };
        }

        foreach (char character in collapsed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.Control
                || (category == UnicodeCategory.Format
                    && character is not (ZeroWidthNonJoiner or ZeroWidthJoiner)))
            {
                throw new DomainValidationException(
                    "A display name must not contain control or format characters.")
                {
                    Code = "account.display_name.control_characters",
                };
            }
        }

        return new MemberDisplayName(collapsed);
    }

    /// <summary>Trims, and collapses every inner run of whitespace to one space.</summary>
    private static string Collapse(string value)
    {
        StringBuilder collapsed = new(value.Length);
        bool pendingSpace = false;

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = collapsed.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(character);
        }

        return collapsed.ToString();
    }

    public override string ToString() => Value;
}
