using OmniAI.Domain.Accounts;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Accounts;

/// <summary>Phase 37.3: languages, audit records, account emails and the emailed invitation token.</summary>
public sealed class AccountDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en", true, UserLanguage.English)]
    [InlineData("ar", true, UserLanguage.Arabic)]
    [InlineData("AR", false, default(UserLanguage))]
    [InlineData("ar-SA", false, default(UserLanguage))]
    [InlineData("fr", false, default(UserLanguage))]
    [InlineData("", false, default(UserLanguage))]
    [InlineData(null, false, default(UserLanguage))]
    public void Only_the_two_exact_codes_parse(string? code, bool parses, UserLanguage expected)
    {
        Assert.Equal(parses, UserLanguages.TryParse(code, out UserLanguage language));

        if (parses)
        {
            Assert.Equal(expected, language);
            Assert.Equal(code, UserLanguages.Code(language));
        }
    }

    [Theory]
    [InlineData("ar-SA;q=0.9", UserLanguage.Arabic)]
    [InlineData("ar-SA,ar;q=0.9,en;q=0.8", UserLanguage.Arabic)]
    [InlineData("en-US,en;q=0.9,ar;q=0.8", UserLanguage.English)]
    [InlineData("fr-FR, ar;q=0.5, en;q=0.4", UserLanguage.Arabic)]
    [InlineData("en;q=0.5, ar;q=0.5", UserLanguage.English)]
    [InlineData("AR", UserLanguage.Arabic)]
    [InlineData("fr, *;q=0.5", null)]
    [InlineData("ar;q=0, en;q=0", null)]
    [InlineData("ar;q=nonsense", UserLanguage.Arabic)]
    [InlineData(";;;,,", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Accept_language_gives_the_first_supported_primary_tag_by_quality(string? header, UserLanguage? expected) =>
        Assert.Equal(expected, UserLanguages.FromAcceptLanguage(header));

    [Fact]
    public void An_account_without_a_language_reads_english() =>
        Assert.Equal(UserLanguage.English, UserLanguages.OrDefault(null));

    [Fact]
    public void A_security_event_kind_must_be_one_of_the_closed_list()
    {
        Assert.Throws<DomainValidationException>(() => SecurityEvent.Record("auth.something.else", Now));
        Assert.Equal(SecurityEventKinds.All.Count, SecurityEventKinds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(SecurityEventKinds.All, kind => Assert.InRange(kind.Length, 1, SecurityEvent.MaxKindLength));
    }

    [Fact]
    public void A_security_event_holds_keys_of_bounded_length_and_drops_empty_references()
    {
        SecurityEvent recorded = SecurityEvent.Record(
            SecurityEventKinds.PasswordResetRequested,
            Now,
            userId: null,
            subjectKey: new string('k', 22),
            clientNetworkKey: new string('n', 22));

        Assert.Null(recorded.UserId);
        Assert.Null(recorded.TenantId);
        Assert.Equal(Now, recorded.OccurredAt);

        Assert.Throws<DomainValidationException>(() =>
            SecurityEvent.Record(SecurityEventKinds.PasswordResetRequested, Now, subjectKey: new string('k', 33)));
        Assert.Throws<DomainValidationException>(() =>
            SecurityEvent.Record(SecurityEventKinds.PasswordResetRequested, Now, clientNetworkKey: string.Empty));
    }

    [Fact]
    public void An_account_email_names_an_account_or_an_invitation_never_both()
    {
        UserId user = UserId.New();
        AccountEmail reset = AccountEmail.ForUser(user, AccountEmailKind.PasswordReset, Now);

        Assert.Equal(user, reset.UserId);
        Assert.Null(reset.InvitationId);
        Assert.Null(reset.TenantId);
        Assert.Equal(AccountEmailStatus.Pending, reset.Status);
        Assert.Equal(Now, reset.AvailableAt);

        Assert.Throws<DomainValidationException>(() => AccountEmail.ForUser(user, AccountEmailKind.Invitation, Now));

        TenantInvitation invitation = Invitation(out _);
        AccountEmail invited = AccountEmail.ForInvitation(invitation, Now);

        Assert.Null(invited.UserId);
        Assert.Equal(invitation.Id, invited.InvitationId);
        Assert.Equal(invitation.TenantId, invited.TenantId);
        Assert.Equal(AccountEmailKind.Invitation, invited.Kind);
    }

    [Fact]
    public void Either_token_redeems_an_invitation_and_only_its_own()
    {
        TenantInvitation invitation = Invitation(out InvitationTokenHash copied);
        InvitationTokenHash emailed = Hash(2);

        Assert.True(invitation.IsRedeemableWith(copied));
        Assert.False(invitation.IsRedeemableWith(emailed));

        invitation.IssueEmailToken(emailed, Now);

        Assert.True(invitation.IsRedeemableWith(copied));
        Assert.True(invitation.IsRedeemableWith(emailed));
        Assert.False(invitation.IsRedeemableWith(Hash(3)));

        // An all-zero digest is the stand-in for "no emailed token" and never matches.
        TenantInvitation unsent = Invitation(out _);
        Assert.False(unsent.IsRedeemableWith(InvitationTokenHash.From(new byte[InvitationTokenHash.Length])));
    }

    [Fact]
    public void An_emailed_token_is_issued_only_while_the_invitation_is_open_and_differs_from_the_copied_one()
    {
        TenantInvitation invitation = Invitation(out InvitationTokenHash copied);

        Assert.Throws<DomainValidationException>(() => invitation.IssueEmailToken(copied, Now));
        Assert.Throws<DomainStateException>(() => invitation.IssueEmailToken(Hash(4), Now.AddDays(30)));

        invitation.Revoke(Now);
        Assert.Throws<DomainStateException>(() => invitation.IssueEmailToken(Hash(5), Now));
    }

    [Fact]
    public void An_invitation_keeps_the_language_it_was_issued_with()
    {
        Assert.Equal(UserLanguage.Arabic, Invitation(out _, UserLanguage.Arabic).Language);
        Assert.Null(Invitation(out _).Language);
    }

    private static TenantInvitation Invitation(out InvitationTokenHash copied, UserLanguage? language = null)
    {
        copied = Hash(1);

        return TenantInvitation.Issue(
            TenantId.From(Guid.NewGuid()),
            EmailAddress.Create("invitee@example.test"),
            UserId.New(),
            copied,
            Array.Empty<TenantRoleId>(),
            Now,
            Now.AddDays(7),
            language);
    }

    private static InvitationTokenHash Hash(byte seed)
    {
        byte[] bytes = new byte[InvitationTokenHash.Length];
        Array.Fill(bytes, seed);

        return InvitationTokenHash.From(bytes);
    }
}
