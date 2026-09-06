using OmniAI.Domain;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Users;

/// <summary>
/// The rule for deciding whether two addresses are the same account.
/// </summary>
/// <remarks>
/// This is a security type, not a formatting one. Getting the comparison wrong in
/// either direction is a defect: too strict locks a legitimate invitee out of
/// their own invitation, and too loose lets somebody accept an invitation written
/// to a different person.
/// </remarks>
public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("person@example.test")]
    [InlineData("first.last@example.co.uk")]
    [InlineData("someone+tag@example.test")]
    public void A_usable_address_is_accepted(string value)
    {
        EmailAddress email = EmailAddress.Create(value);

        Assert.Equal(value, email.Value);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_rejected()
    {
        // Somebody pasting an address picks up a space more often than not, and
        // refusing it would be pedantry the reader has to diagnose.
        Assert.Equal("person@example.test", EmailAddress.Create("  person@example.test  ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign.example.test")]
    [InlineData("@example.test")]
    [InlineData("person@")]
    [InlineData("person@localhost")]
    [InlineData("two@at@example.test")]
    [InlineData("person name@example.test")]
    [InlineData("person\t@example.test")]
    public void An_unusable_address_is_refused(string? value) =>
        Assert.Throws<DomainValidationException>(() => EmailAddress.Create(value));

    [Fact]
    public void An_address_longer_than_the_limit_is_refused_rather_than_shortened()
    {
        // Truncating would produce a different address, which may well belong to
        // somebody else.
        string tooLong = new string('a', EmailAddress.MaxLength) + "@example.test";

        Assert.Throws<DomainValidationException>(() => EmailAddress.Create(tooLong));
    }

    [Fact]
    public void Two_addresses_differing_only_in_case_are_the_same_account()
    {
        EmailAddress written = EmailAddress.Create("Person@Example.Test");
        EmailAddress signedIn = EmailAddress.Create("person@example.test");

        Assert.True(written.Matches(signedIn));
    }

    [Fact]
    public void Equality_uses_the_same_rule_as_matching()
    {
        // The trap this guards against: a record struct's generated equality would
        // compare the display form too, so == and Matches would disagree and the
        // security-relevant call site would be whichever one somebody wrote.
        EmailAddress written = EmailAddress.Create("Person@Example.Test");
        EmailAddress signedIn = EmailAddress.Create("person@example.test");

        Assert.Equal(written, signedIn);
        Assert.True(written == signedIn);
        Assert.Equal(written.GetHashCode(), signedIn.GetHashCode());
    }

    [Fact]
    public void Different_addresses_do_not_match()
    {
        Assert.NotEqual(
            EmailAddress.Create("alice@example.test"),
            EmailAddress.Create("mallory@example.test"));
    }

    [Fact]
    public void The_display_form_keeps_the_case_it_was_written_in()
    {
        // Shown back to somebody in a case they did not choose reads as a mistake
        // the platform made.
        EmailAddress email = EmailAddress.Create("Person@Example.Test");

        Assert.Equal("Person@Example.Test", email.Value);
        Assert.Equal("PERSON@EXAMPLE.TEST", email.Canonical);
    }

    [Fact]
    public void A_stored_address_keeps_the_canonical_form_it_was_matched_on()
    {
        // Re-deriving on read would mean a change to canonicalisation silently
        // turned stored rows into different addresses.
        EmailAddress stored = EmailAddress.FromStored("Person@Example.Test", "SOMETHING-ELSE");

        Assert.Equal("SOMETHING-ELSE", stored.Canonical);
    }

    [Fact]
    public void The_default_value_matches_nothing_including_itself()
    {
        // A member row whose address could not be read produces the default. It
        // must never satisfy an invitation.
        EmailAddress missing = default;

        Assert.True(missing.IsEmpty);
        Assert.False(missing.Matches(EmailAddress.Create("person@example.test")));
        Assert.False(EmailAddress.Create("person@example.test").Matches(missing));
    }

    [Fact]
    public void Reading_reports_failure_without_throwing_when_asked()
    {
        Assert.False(EmailAddress.TryCreate("not-an-address", out _));
        Assert.True(EmailAddress.TryCreate("person@example.test", out EmailAddress parsed));
        Assert.Equal("person@example.test", parsed.Value);
    }
}
