using OmniAI.Domain.AiAgents;

namespace OmniAI.Domain.Tests.AiAgents;

public sealed class AgentInstructionsTests
{
    [Fact]
    public void Create_accepts_realistic_instructions()
    {
        const string value =
            "You are the sales assistant for Acme Trading. Answer questions about " +
            "pricing and availability, and hand over to a human for anything else.";

        Assert.Equal(value, AgentInstructions.Create(value).Value);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        Assert.Equal(
            "You are the sales assistant.",
            AgentInstructions.Create("\n\n   You are the sales assistant.   \n\n").Value);
    }

    [Fact]
    public void Create_preserves_interior_line_breaks_exactly()
    {
        // The rule that matters most for this type. An author who writes numbered
        // rules on separate lines means them to stay on separate lines; collapsing
        // that would change what the agent was told to do.
        const string multiline = "Rules:\n1. Be brief.\n2. Never quote a discount.\n3. Escalate refunds.";

        AgentInstructions instructions = AgentInstructions.Create(multiline);

        Assert.Equal(multiline, instructions.Value);
        Assert.Equal(3, instructions.Value.Split('\n').Length - 1);
    }

    [Fact]
    public void Create_preserves_blank_lines_indentation_and_tabs()
    {
        const string formatted = "Scope:\n\n\tPricing questions\n\tAvailability questions\n\n  Do not discuss refunds.";

        Assert.Equal(formatted, AgentInstructions.Create(formatted).Value);
    }

    [Fact]
    public void Create_preserves_windows_line_endings()
    {
        const string crlf = "First line.\r\nSecond line.";

        Assert.Equal(crlf, AgentInstructions.Create(crlf).Value);
    }

    [Fact]
    public void Create_trims_only_the_outside_of_a_multiline_value()
    {
        const string padded = "  \n Rules:\n  Be brief. \n  ";

        Assert.Equal("Rules:\n  Be brief.", AgentInstructions.Create(padded).Value);
    }

    [Fact]
    public void Create_preserves_non_latin_instructions()
    {
        const string arabic = "أنت مساعد المبيعات لشركة أكمي.\nأجب باللغة العربية.";

        Assert.Equal(arabic, AgentInstructions.Create(arabic).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("   \t\r\n   ")]
    public void Create_rejects_blank_instructions(string? value)
    {
        Assert.Throws<DomainValidationException>(() => AgentInstructions.Create(value));
    }

    [Fact]
    public void Create_accepts_instructions_of_exactly_the_maximum_length()
    {
        string atLimit = new('a', AgentInstructions.MaxLength);

        Assert.Equal(AgentInstructions.MaxLength, AgentInstructions.Create(atLimit).Value.Length);
    }

    [Fact]
    public void Create_rejects_instructions_one_character_over_the_maximum()
    {
        string overLimit = new('a', AgentInstructions.MaxLength + 1);

        Assert.Throws<DomainValidationException>(() => AgentInstructions.Create(overLimit));
    }

    [Fact]
    public void Create_measures_the_maximum_after_trimming()
    {
        string padded = $"\n  {new string('a', AgentInstructions.MaxLength)}  \n";

        Assert.Equal(AgentInstructions.MaxLength, AgentInstructions.Create(padded).Value.Length);
    }

    [Fact]
    public void Create_does_not_truncate_over_long_instructions()
    {
        string overLimit = new('a', AgentInstructions.MaxLength + 500);

        DomainValidationException failure =
            Assert.Throws<DomainValidationException>(() => AgentInstructions.Create(overLimit));

        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_maximum_is_a_platform_bound_not_a_provider_context_limit()
    {
        // Guards the decision, not the number: if someone later "aligns" this with
        // a model's token window, the Domain has taken on a vendor's pricing
        // decision. It is far above any realistic hand-written instruction set.
        Assert.Equal(20_000, AgentInstructions.MaxLength);
    }

    [Theory]
    [InlineData("Be brief.\0Never quote a discount.")]
    [InlineData("Be brief.\u0007Never quote a discount.")]
    [InlineData("Be brief.\u001bNever quote a discount.")]
    public void Create_rejects_control_characters_other_than_tab_and_line_breaks(string value)
    {
        Assert.Throws<DomainValidationException>(() => AgentInstructions.Create(value));
    }

    [Fact]
    public void Instructions_with_the_same_value_are_equal()
    {
        Assert.Equal(
            AgentInstructions.Create("Be brief."),
            AgentInstructions.Create("  Be brief.  "));
    }

    [Fact]
    public void Instructions_with_different_values_are_not_equal()
    {
        Assert.NotEqual(
            AgentInstructions.Create("Be brief."),
            AgentInstructions.Create("Be thorough."));
    }

    [Fact]
    public void Instructions_differing_only_in_interior_formatting_are_not_equal()
    {
        // Because interior formatting is preserved, it is part of the value.
        Assert.NotEqual(
            AgentInstructions.Create("One.\nTwo."),
            AgentInstructions.Create("One. Two."));
    }

    [Fact]
    public void ToString_is_the_instruction_text()
    {
        Assert.Equal("Be brief.", AgentInstructions.Create("Be brief.").ToString());
    }
}
