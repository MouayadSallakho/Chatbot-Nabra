using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Users;

/// <summary>Phase 39.1 (note 7): the rules of a member's display name.</summary>
public sealed class MemberDisplayNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t   ")]
    public void Blank_means_no_name(string? value)
    {
        Assert.Null(MemberDisplayName.CreateOrNone(value));
    }

    [Theory]
    [InlineData("  Sara  ", "Sara")]
    [InlineData("Sara   Haddad", "Sara Haddad")]
    [InlineData(" Sara \t\t Haddad  Jr ", "Sara Haddad Jr")]
    [InlineData("سارة   حداد", "سارة حداد")]
    public void It_is_trimmed_and_inner_whitespace_runs_become_one_space(string value, string expected)
    {
        Assert.Equal(expected, MemberDisplayName.CreateOrNone(value)!.Value);
    }

    [Fact]
    public void Eighty_characters_pass_and_eighty_one_are_refused_with_the_limit()
    {
        Assert.Equal(80, MemberDisplayName.CreateOrNone(new string('a', 80))!.Value.Length);

        // Spaces around the name do not count: the bound is on the stored text.
        Assert.Equal(80, MemberDisplayName.CreateOrNone("  " + new string('a', 80) + "  ")!.Value.Length);

        DomainValidationException refused = Assert.Throws<DomainValidationException>(
            () => MemberDisplayName.CreateOrNone(new string('a', 81)));

        Assert.Equal("account.display_name.too_long", refused.Code);
        Assert.Equal(80, refused.Limit);
        Assert.Equal("characters", refused.LimitUnit);
        Assert.Null(refused.Field);
    }

    [Fact]
    public void The_length_is_counted_in_utf16_code_units()
    {
        // Forty emoji are eighty code units: the bound, exactly.
        string forty = string.Concat(Enumerable.Repeat("\U0001F600", 40));
        Assert.Equal(80, MemberDisplayName.CreateOrNone(forty)!.Value.Length);

        Assert.Throws<DomainValidationException>(() => MemberDisplayName.CreateOrNone(forty + "a"));
    }

    [Theory]
    [InlineData("Sara\u0007")]
    [InlineData("Sara\u0000Haddad")]
    [InlineData("‎Sara")]
    [InlineData("Sara‮Haddad")]
    [InlineData("Sara﻿")]
    [InlineData("Sara­")]
    public void Control_and_format_characters_are_refused(string value)
    {
        DomainValidationException refused = Assert.Throws<DomainValidationException>(
            () => MemberDisplayName.CreateOrNone(value));

        Assert.Equal("account.display_name.control_characters", refused.Code);
    }

    [Theory]
    [InlineData("ل‌ا")]
    [InlineData("\U0001F469‍\U0001F4BB Sara")]
    public void The_two_joiners_arabic_and_emoji_need_are_kept(string value)
    {
        Assert.Equal(value, MemberDisplayName.CreateOrNone(value)!.Value);
    }

    [Fact]
    public void Line_breaks_are_whitespace_and_become_a_space()
    {
        Assert.Equal("Sara Haddad", MemberDisplayName.CreateOrNone("Sara\r\nHaddad")!.Value);
    }
}
