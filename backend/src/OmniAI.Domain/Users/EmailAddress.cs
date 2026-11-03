using System.Globalization;

namespace OmniAI.Domain.Users;

/// <summary>
/// An email address, and the one rule this platform uses for deciding whether two
/// of them are the same.
/// </summary>
/// <remarks>
/// <strong>It exists for the comparison, not the validation.</strong> An
/// invitation names an address, and acceptance has to decide whether the account
/// signing in is the account that was invited. Getting that comparison wrong in
/// either direction is a security defect: too strict and a legitimate invitee is
/// locked out of their own invitation, too loose and somebody accepts an
/// invitation addressed to a different person. So the rule lives in one type that
/// both sides use, rather than in two <c>string.Equals</c> calls that could drift.
/// <para>
/// <strong>Canonical form is the trimmed address upper-cased invariantly.</strong>
/// That is deliberately the same rule ASP.NET Core Identity uses for its own
/// normalised email column, so an invitation and the account it is matched
/// against are compared the way the identity provider already compares accounts.
/// A second, cleverer rule here -- stripping dots, honouring <c>+</c> tags,
/// lower-casing only the domain -- would be a rule about somebody else's mail
/// server, and it would disagree with Identity about who is who.
/// </para>
/// <para>
/// <strong>Validation is deliberately shallow.</strong> Required, trimmed, one
/// <c>@</c> with something either side, no whitespace, no control characters, and
/// a length limit. There is no regular expression attempting RFC 5322: every such
/// expression in the wild rejects addresses that genuinely deliver, and this
/// platform has no way to prove an address exists anyway. Deliverability is
/// established by somebody receiving the invitation link, which is the only proof
/// that means anything.
/// </para>
/// <para>
/// Nothing here truncates. An address longer than the limit is refused, because
/// silently shortening one produces a different address that may belong to
/// somebody else.
/// </para>
/// </remarks>
public readonly record struct EmailAddress : IComparable<EmailAddress>
{
    /// <summary>
    /// The longest address this platform stores.
    /// </summary>
    /// <remarks>
    /// Matches the identity provider's own email column, so an address that can be
    /// registered can also be invited, and neither store can hold one the other
    /// cannot.
    /// </remarks>
    public const int MaxLength = 256;

    private EmailAddress(string value, string canonical)
    {
        Value = value;
        Canonical = canonical;
    }

    /// <summary>The address as it was written, trimmed.</summary>
    /// <remarks>
    /// Kept for display, because an address shown back to somebody in a case they
    /// did not choose reads as a mistake the platform made.
    /// </remarks>
    public string Value { get; }

    /// <summary>The form two addresses are compared in.</summary>
    public string Canonical { get; }

    /// <summary>Whether this is the default value rather than a real address.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Reads an address, or refuses it.</summary>
    /// <exception cref="DomainValidationException">
    /// It is missing, blank, too long, carries whitespace or control characters,
    /// or is not shaped like an address.
    /// </exception>
    public static EmailAddress Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("An email address is required.")
            {
                Code = "email.required",
            };
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"An email address must be {MaxLength} characters or fewer.")
            {
                Code = "email.too_long",
            };
        }

        foreach (char character in trimmed)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                throw new DomainValidationException(
                    "An email address must not contain spaces or control characters.")
                {
                    Code = "email.invalid_characters",
                };
            }
        }

        int at = trimmed.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0
            || at != trimmed.LastIndexOf('@')
            || at == trimmed.Length - 1
            || !trimmed.Contains('.', StringComparison.Ordinal))
        {
            throw new DomainValidationException(
                $"'{trimmed}' is not an email address this platform can use.")
            {
                Code = "email.invalid",
            };
        }

        return new EmailAddress(trimmed, trimmed.ToUpperInvariant());
    }

    /// <summary>Reads an address, or reports that it could not be read.</summary>
    public static bool TryCreate(string? value, out EmailAddress email)
    {
        try
        {
            email = Create(value);

            return true;
        }
        catch (DomainValidationException)
        {
            email = default;

            return false;
        }
    }

    /// <summary>
    /// Rebuilds an address already stored, keeping its recorded canonical form.
    /// </summary>
    /// <remarks>
    /// For persistence only. Reading a row must not re-derive the canonical form:
    /// if this platform's canonicalisation ever changes, stored rows keep the form
    /// they were matched on until they are deliberately migrated, rather than
    /// silently becoming a different address on the next read.
    /// </remarks>
    public static EmailAddress FromStored(string value, string canonical) =>
        new(value, canonical);

    /// <summary>Whether two addresses are the same account, canonically.</summary>
    /// <remarks>
    /// The same answer <c>==</c> gives, and named so that a security check reads
    /// as one at the call site.
    /// </remarks>
    public bool Matches(EmailAddress other) => Equals(other);

    /// <summary>
    /// Equality is canonical, not literal.
    /// </summary>
    /// <remarks>
    /// <strong>Deliberately replacing the generated member-wise equality.</strong>
    /// The compiler's version would compare the display form too, so
    /// <c>Person@Example.test</c> and <c>person@example.test</c> would be unequal
    /// under <c>==</c> and equal under <see cref="Matches"/> -- two answers to one
    /// question, with the security-relevant call site being whichever one somebody
    /// happened to write. There is one rule, and it is this one.
    /// </remarks>
    public bool Equals(EmailAddress other) =>
        string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Canonical ?? string.Empty);

    /// <summary>Ordered by canonical form, so listings are stable.</summary>
    public int CompareTo(EmailAddress other) =>
        string.CompareOrdinal(Canonical, other.Canonical);

    public override string ToString() => Value;

    public static bool operator <(EmailAddress left, EmailAddress right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(EmailAddress left, EmailAddress right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(EmailAddress left, EmailAddress right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(EmailAddress left, EmailAddress right) =>
        left.CompareTo(right) >= 0;
}
