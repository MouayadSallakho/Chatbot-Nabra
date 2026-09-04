using OmniAI.Domain.AiAgents;

namespace OmniAI.Domain.Tests.AiAgents;

public sealed class AiAgentNameTests
{
    [Theory]
    [InlineData("Sales Assistant")]
    [InlineData("Customer Support")]
    [InlineData("Booking Assistant")]
    [InlineData("A")]
    [InlineData("مساعد المبيعات")]
    [InlineData("Assistant Nº1 — Ventes")]
    public void Create_accepts_a_realistic_display_name(string value)
    {
        Assert.Equal(value, AiAgentName.Create(value).Value);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        Assert.Equal("Sales Assistant", AiAgentName.Create("   Sales Assistant   ").Value);
    }

    [Fact]
    public void Create_preserves_interior_spacing()
    {
        // Trimming is the only normalisation. Collapsing interior spaces would be
        // repairing a value rather than validating it.
        Assert.Equal("Sales   Assistant", AiAgentName.Create("  Sales   Assistant  ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("      ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \t\r\n ")]
    public void Create_rejects_a_blank_name(string? value)
    {
        Assert.Throws<DomainValidationException>(() => AiAgentName.Create(value));
    }

    [Fact]
    public void Create_accepts_a_name_of_exactly_the_maximum_length()
    {
        string atLimit = new('a', AiAgentName.MaxLength);

        AiAgentName name = AiAgentName.Create(atLimit);

        Assert.Equal(AiAgentName.MaxLength, name.Value.Length);
    }

    [Fact]
    public void Create_rejects_a_name_one_character_over_the_maximum()
    {
        string overLimit = new('a', AiAgentName.MaxLength + 1);

        Assert.Throws<DomainValidationException>(() => AiAgentName.Create(overLimit));
    }

    [Fact]
    public void Create_measures_the_maximum_after_trimming()
    {
        // Surrounding whitespace must not count against the caller.
        string padded = $"  {new string('a', AiAgentName.MaxLength)}  ";

        Assert.Equal(AiAgentName.MaxLength, AiAgentName.Create(padded).Value.Length);
    }

    [Fact]
    public void Create_does_not_truncate_an_over_long_name()
    {
        string overLimit = new('a', AiAgentName.MaxLength + 50);

        DomainValidationException failure =
            Assert.Throws<DomainValidationException>(() => AiAgentName.Create(overLimit));

        // The message says so explicitly, because silent truncation would mean a
        // tenant's agent quietly acquiring a different name from the one they typed.
        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Sales\nAssistant")]
    [InlineData("Sales\rAssistant")]
    [InlineData("Sales\tAssistant")]
    [InlineData("Sales\0Assistant")]
    public void Create_rejects_control_characters_including_line_breaks_and_tabs(string value)
    {
        // Stricter than AgentInstructions on purpose: a single-line label has no
        // use for interior formatting, and allowing it would corrupt lists and logs.
        Assert.Throws<DomainValidationException>(() => AiAgentName.Create(value));
    }

    [Fact]
    public void Names_with_the_same_value_are_equal()
    {
        Assert.Equal(AiAgentName.Create("Sales Assistant"), AiAgentName.Create("  Sales Assistant  "));
    }

    [Fact]
    public void Names_with_different_values_are_not_equal()
    {
        Assert.NotEqual(AiAgentName.Create("Sales Assistant"), AiAgentName.Create("Support"));
    }

    [Fact]
    public void Names_are_case_sensitive()
    {
        // No case folding: unlike a slug, a display name is shown as typed, and
        // lowercasing it would change what the tenant sees.
        Assert.NotEqual(AiAgentName.Create("Sales Assistant"), AiAgentName.Create("sales assistant"));
    }

    [Fact]
    public void ToString_is_the_name_itself()
    {
        Assert.Equal("Sales Assistant", AiAgentName.Create("Sales Assistant").ToString());
    }
}
