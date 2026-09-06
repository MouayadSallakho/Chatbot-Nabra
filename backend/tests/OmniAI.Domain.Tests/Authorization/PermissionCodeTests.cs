using OmniAI.Domain;
using OmniAI.Domain.Authorization;

namespace OmniAI.Domain.Tests.Authorization;

/// <summary>
/// The type that carries the platform's security contract.
/// </summary>
/// <remarks>
/// Every rule here exists to stop two codes that a person would read as the same
/// thing from becoming two different grants, or one malformed value from becoming
/// a grant at all.
/// </remarks>
public sealed class PermissionCodeTests
{
    [Fact]
    public void A_well_formed_code_is_accepted()
    {
        Assert.Equal("ai-agents.edit", PermissionCode.Create("ai-agents.edit").Value);
    }

    [Theory]
    [InlineData("AI-Agents.Edit")]
    [InlineData("ai-agents.EDIT")]
    [InlineData("  ai-agents.edit  ")]
    public void Case_and_surrounding_whitespace_are_canonicalised(string supplied)
    {
        // Not cosmetic. Two codes differing only in case would be two rows in
        // role_permissions and two different answers from an authorisation check,
        // for a value every person involved would read as one capability.
        Assert.Equal(PermissionCode.Create("ai-agents.edit"), PermissionCode.Create(supplied));
    }

    [Fact]
    public void Canonicalisation_does_not_depend_on_the_machines_culture()
    {
        // ToLowerInvariant rather than ToLower. Under a Turkish culture 'I'
        // lowercases to a dotless i, so a culture-sensitive fold would make two
        // machines disagree about whether a stored grant matches a required
        // permission -- which is an authorisation bug that only appears on
        // somebody else's laptop.
        Assert.Equal("roles.view", PermissionCode.Create("ROLES.VIEW").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_rejected(string? supplied)
    {
        Assert.Throws<DomainValidationException>(() => PermissionCode.Create(supplied));
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("manage")]
    public void A_code_without_a_group_and_an_action_is_rejected(string supplied)
    {
        // The grammar is deliberately narrow. A bare word would make it far too
        // easy to invent a plausible-looking permission that nothing recognises,
        // and would remove the grouping the role editor renders.
        Assert.Throws<DomainValidationException>(() => PermissionCode.Create(supplied));
    }

    [Theory]
    [InlineData("roles manage")]
    [InlineData("roles_manage")]
    [InlineData("roles.manage!")]
    [InlineData("rôles.manage")]
    [InlineData("roles/manage")]
    public void A_code_outside_the_grammar_is_rejected_rather_than_repaired(string supplied)
    {
        // Rejected, never rewritten. Silently stripping an underscore would turn
        // one caller's typo into a different, real capability.
        Assert.Throws<DomainValidationException>(() => PermissionCode.Create(supplied));
    }

    [Theory]
    [InlineData(".roles.manage")]
    [InlineData("roles.manage.")]
    [InlineData("-roles.manage")]
    [InlineData("roles.manage-")]
    [InlineData("roles..manage")]
    [InlineData("roles.--manage")]
    public void Separators_may_not_lead_trail_or_repeat(string supplied)
    {
        Assert.Throws<DomainValidationException>(() => PermissionCode.Create(supplied));
    }

    [Fact]
    public void A_code_longer_than_the_limit_is_rejected_and_not_truncated()
    {
        string tooLong = "a." + new string('b', PermissionCode.MaxLength);

        DomainValidationException failure =
            Assert.Throws<DomainValidationException>(() => PermissionCode.Create(tooLong));

        // Silent truncation would produce a shorter code that might coincide with
        // a real capability, so the message says plainly that nothing was cut.
        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_code_at_the_limit_is_accepted()
    {
        string atLimit = "a." + new string('b', PermissionCode.MaxLength - 2);

        Assert.Equal(PermissionCode.MaxLength, PermissionCode.Create(atLimit).Value.Length);
    }

    [Fact]
    public void Two_codes_with_the_same_value_are_equal()
    {
        // Value equality is what makes a HashSet of codes work, which is what the
        // union of a member's roles is built on.
        Assert.Equal(PermissionCode.Create("roles.view"), PermissionCode.Create("roles.view"));
        Assert.NotEqual(PermissionCode.Create("roles.view"), PermissionCode.Create("roles.manage"));
    }

    [Fact]
    public void The_default_value_is_reported_as_empty()
    {
        // Reachable only through default(PermissionCode), which no factory
        // produces. It matters because a default would compare equal to another
        // default rather than to any real grant.
        Assert.True(default(PermissionCode).IsEmpty);
        Assert.False(PermissionCode.Create("roles.view").IsEmpty);
    }

    [Fact]
    public void Try_create_reports_failure_instead_of_throwing()
    {
        Assert.False(PermissionCode.TryCreate("not a code", out PermissionCode rejected));
        Assert.True(rejected.IsEmpty);

        Assert.True(PermissionCode.TryCreate("roles.view", out PermissionCode accepted));
        Assert.Equal("roles.view", accepted.Value);
    }

    [Fact]
    public void Codes_order_by_their_canonical_value()
    {
        // Ordering exists so that two roles granting the same set produce the
        // same list, in responses and in assertions alike.
        string[] unsorted = ["roles.view", "ai-agents.edit", "workspace.view"];
        string[] expected = ["ai-agents.edit", "roles.view", "workspace.view"];

        IEnumerable<string> sorted = unsorted
            .Select(PermissionCode.Create)
            .Order()
            .Select(code => code.Value);

        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void The_string_form_is_the_code_itself()
    {
        Assert.Equal("ai-agents.preview", PermissionCode.Create("ai-agents.preview").ToString());
    }
}
