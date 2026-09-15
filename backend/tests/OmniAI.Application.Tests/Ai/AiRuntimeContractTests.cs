using OmniAI.Application.Ai;

namespace OmniAI.Application.Tests.Ai;

/// <summary>
/// The provider-independent AI runtime contracts themselves.
/// </summary>
/// <remarks>
/// These types carry the platform's only rules about what may be sent to, and
/// accepted from, an AI text generator. They are tested directly because every
/// adapter written later will depend on them behaving exactly this way.
/// </remarks>
public sealed class AiRuntimeContractTests
{
    /// <summary>The only roles a conversational turn may carry.</summary>
    private static readonly AiTurnRole[] SupportedRoles = [AiTurnRole.User, AiTurnRole.Assistant];

    /// <summary>
    /// The only properties a generation request may expose.
    /// </summary>
    /// <remarks>
    /// Grounding joined the two original slots in Phase 17. It is content rather
    /// than provider configuration -- reference material the answer may draw on --
    /// and it is a third property precisely so it can never be merged into either
    /// of the other two.
    /// </remarks>
    private static readonly string[] RequestProperties =
        ["Grounding", "Instructions", "Turns"];

    /// <summary>The turns a three-turn ordering check expects, in order.</summary>
    private static readonly string[] OrderedTurns = ["First", "Second", "Third"];

    // -------------------------------------------------------------------- turns

    [Fact]
    public void A_user_turn_carries_the_user_role()
    {
        AiTurn turn = AiTurn.FromUser("Which laptop?");

        Assert.Equal(AiTurnRole.User, turn.Role);
        Assert.Equal("Which laptop?", turn.Text);
    }

    [Fact]
    public void An_assistant_turn_carries_the_assistant_role()
    {
        // Unused by preview, present because a conversation runtime must be able
        // to replay the agent's own past replies through the same contract.
        AiTurn turn = AiTurn.FromAssistant("It depends what you need it for.");

        Assert.Equal(AiTurnRole.Assistant, turn.Role);
    }

    [Fact]
    public void Only_user_and_assistant_roles_exist()
    {
        // The omission is the design: with no System or Developer role, caller
        // text has no way to become a privileged instruction. A future edit that
        // adds one has to change this test, which is the point.
        Assert.Equal(SupportedRoles, Enum.GetValues<AiTurnRole>());
    }

    [Fact]
    public void No_factory_accepts_an_arbitrary_role()
    {
        // There is no public way to construct a turn with a role of the caller's
        // choosing, so no call site can invent one even by accident.
        Assert.DoesNotContain(
            typeof(AiTurn).GetMethods().Where(method => method.IsStatic && method.IsPublic),
            method => method.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(AiTurnRole)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void Blank_turn_text_is_refused(string? text)
    {
        Assert.Throws<AiTurnValidationException>(() => AiTurn.FromUser(text));
        Assert.Throws<AiTurnValidationException>(() => AiTurn.FromAssistant(text));
    }

    [Fact]
    public void Turn_text_at_the_maximum_is_accepted()
    {
        string longest = new('a', AiTurn.MaxTextLength);

        Assert.Equal(longest, AiTurn.FromUser(longest).Text);
    }

    [Fact]
    public void Turn_text_over_the_maximum_is_refused_rather_than_truncated()
    {
        AiTurnValidationException failure = Assert.Throws<AiTurnValidationException>(
            () => AiTurn.FromUser(new string('a', AiTurn.MaxTextLength + 1)));

        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Length_is_measured_after_trimming()
    {
        // Trailing whitespace must not be able to push an acceptable message over
        // the bound.
        string padded = new string('a', AiTurn.MaxTextLength) + "      \n";

        Assert.Equal(AiTurn.MaxTextLength, AiTurn.FromUser(padded).Text.Length);
    }

    [Fact]
    public void Only_the_ends_are_trimmed()
    {
        Assert.Equal("a\n\n  b", AiTurn.FromUser("  \n a\n\n  b \t ").Text);
    }

    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("carriage\rreturn")]
    [InlineData("tab\tseparated")]
    public void Ordinary_formatting_characters_are_allowed(string text)
    {
        Assert.Equal(text, AiTurn.FromUser(text).Text);
    }

    [Theory]
    [InlineData("null\0byte")]
    [InlineData("bell\a")]
    [InlineData("escape[31m")]
    public void Other_control_characters_are_refused(string text)
    {
        // They mean nothing in text a person wrote and corrupt logs, terminals
        // and rendered output downstream.
        Assert.Throws<AiTurnValidationException>(() => AiTurn.FromUser(text));
    }

    [Fact]
    public void The_turn_maximum_sits_below_the_instruction_maximum()
    {
        // Instructions are authored once and read on every turn; a turn is one
        // person's message. Both are platform resource bounds, neither is a
        // provider token limit.
        Assert.True(AiTurn.MaxTextLength < Domain.AiAgents.AgentInstructions.MaxLength);
    }

    // ------------------------------------------------------------------ requests

    [Fact]
    public void A_request_keeps_instructions_and_turns_apart()
    {
        AiTextGenerationRequest request = AiTextGenerationRequest.Create(
            "Never quote a discount.",
            [AiTurn.FromUser("Any discount?")]);

        Assert.Equal("Never quote a discount.", request.Instructions);
        Assert.Equal("Any discount?", Assert.Single(request.Turns).Text);
    }

    [Fact]
    public void A_request_never_concatenates()
    {
        // No slot on this type contains both, in either order, with any separator.
        AiTextGenerationRequest request = AiTextGenerationRequest.Create(
            "Never quote a discount.",
            [AiTurn.FromUser("Any discount?")]);

        Assert.DoesNotContain("Any discount?", request.Instructions, StringComparison.Ordinal);

        foreach (AiTurn turn in request.Turns)
        {
            Assert.DoesNotContain("Never quote", turn.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_request_preserves_turn_order()
    {
        AiTextGenerationRequest request = AiTextGenerationRequest.Create(
            "Be helpful.",
            [AiTurn.FromUser("First"), AiTurn.FromAssistant("Second"), AiTurn.FromUser("Third")]);

        Assert.Equal(OrderedTurns, request.Turns.Select(turn => turn.Text));
    }

    [Fact]
    public void A_request_copies_its_turns()
    {
        // A caller must not be able to alter a request after handing it to an
        // adapter that may hold it across an await.
        List<AiTurn> turns = [AiTurn.FromUser("Original")];

        AiTextGenerationRequest request = AiTextGenerationRequest.Create("Be helpful.", turns);

        turns.Add(AiTurn.FromUser("Added afterwards"));
        turns[0] = AiTurn.FromUser("Replaced afterwards");

        Assert.Equal("Original", Assert.Single(request.Turns).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_request_requires_instructions(string? instructions)
    {
        Assert.Throws<ArgumentException>(
            () => AiTextGenerationRequest.Create(instructions!, [AiTurn.FromUser("Hello?")]));
    }

    [Fact]
    public void A_request_requires_at_least_one_turn()
    {
        Assert.Throws<ArgumentException>(
            () => AiTextGenerationRequest.Create("Be helpful.", []));
    }

    [Fact]
    public void A_request_refuses_a_null_turn()
    {
        Assert.Throws<ArgumentException>(
            () => AiTextGenerationRequest.Create("Be helpful.", [null!]));

        Assert.Throws<ArgumentNullException>(
            () => AiTextGenerationRequest.Create("Be helpful.", null!));
    }

    [Fact]
    public void A_request_exposes_nothing_about_a_model_or_provider()
    {
        // Model, temperature, token budget, tools and response format belong to
        // the phase that integrates a real provider. None may appear here first.
        string[] properties =
        [
            .. typeof(AiTextGenerationRequest).GetProperties().Select(property => property.Name)
        ];

        Assert.Equal(RequestProperties, properties.Order(StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------- results

    [Fact]
    public void A_result_carries_the_generated_text()
    {
        Assert.Equal("Certainly.", AiTextGenerationResult.Create("Certainly.").Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t \r\n")]
    public void A_blank_result_cannot_be_constructed(string? text)
    {
        // The boundary is the type itself, so a badly behaved adapter fails where
        // the bad value was produced rather than further up as a symptom.
        Assert.Throws<AiResponseInvalidException>(() => AiTextGenerationResult.Create(text));
    }

    [Fact]
    public void A_result_trims_its_ends_and_keeps_its_interior()
    {
        Assert.Equal(
            "Two options:\n\n- A\n- B",
            AiTextGenerationResult.Create("  Two options:\n\n- A\n- B\n ").Text);
    }

    [Fact]
    public void A_result_exposes_only_text()
    {
        // No model name, provider name, token count, cost, finish reason or
        // request id: none has been observed from a real provider yet.
        string[] properties =
        [
            .. typeof(AiTextGenerationResult).GetProperties()
                .Where(property => property.Name != "EqualityContract")
                .Select(property => property.Name)
        ];

        Assert.Equal("Text", Assert.Single(properties));
    }

    // ------------------------------------------------------------------ contract

    [Fact]
    public void The_generator_contract_has_exactly_one_capability()
    {
        // Not an IAiProvider carrying embeddings, images, speech, moderation,
        // tools, streaming and model management as methods that would throw.
        System.Reflection.MethodInfo method = Assert.Single(typeof(IAiTextGenerator).GetMethods());

        Assert.Equal(nameof(IAiTextGenerator.GenerateAsync), method.Name);
        Assert.Equal(typeof(Task<AiTextGenerationResult>), method.ReturnType);
    }

    [Fact]
    public void The_generator_contract_is_cancellable()
    {
        System.Reflection.ParameterInfo[] parameters =
            typeof(IAiTextGenerator).GetMethods()[0].GetParameters();

        Assert.Equal(typeof(AiTextGenerationRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
    }

    /// <remarks>
    /// <strong>The attribution parameter is the whole of Phase 23's tenant
    /// story.</strong> It is required rather than optional and a parameter rather
    /// than ambient state, so a new call site cannot compile without saying whose
    /// money it spends. An ambient value read from the current request works right
    /// up until the first background worker, and then attributes spend to whatever
    /// happened to be on the stack.
    /// </remarks>
    [Fact]
    public void The_generator_contract_demands_to_know_whose_money_it_is_spending()
    {
        System.Reflection.ParameterInfo attribution =
            typeof(IAiTextGenerator).GetMethods()[0].GetParameters()[1];

        Assert.Equal(typeof(Application.Ai.Usage.AiUsageContext), attribution.ParameterType);
        Assert.False(attribution.IsOptional);
    }

    /// <remarks>The embedding contract carries the same requirement.</remarks>
    [Fact]
    public void The_embedding_contract_demands_to_know_whose_money_it_is_spending()
    {
        System.Reflection.MethodInfo generate = typeof(Application.Knowledge.ITextEmbeddingGenerator)
            .GetMethod(nameof(Application.Knowledge.ITextEmbeddingGenerator.GenerateAsync))!;

        System.Reflection.ParameterInfo attribution = generate.GetParameters()[1];

        Assert.Equal(typeof(Application.Ai.Usage.AiUsageContext), attribution.ParameterType);
        Assert.False(attribution.IsOptional);
    }
}
