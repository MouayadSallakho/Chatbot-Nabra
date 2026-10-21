using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.StandIn;

/// <summary>
/// The local AI stand-in's replies (Phase 37.2).
/// </summary>
/// <remarks>
/// No delay unless a test asks for one, no network and no key: the generator
/// under test is the one the dev API composes, over a recording ledger priced by
/// the real catalogue with the stand-in's built-in rate.
/// </remarks>
public sealed class LocalStandInTextGeneratorTests
{
    private static readonly AiUsageContext Usage =
        AiUsageContext.For(TenantId.New(), AiUsagePurpose.ConversationGeneration);

    private static (LocalStandInTextGenerator Generator, RecordingUsageLedger Ledger) Create(
        int delayMilliseconds = 0)
    {
        RecordingUsageLedger ledger = new(
            AiProviderPricingCatalog.Create(null, includeLocalStandIn: true));

        return (
            new LocalStandInTextGenerator(
                new LocalAiStandInOptions { ReplyDelayMilliseconds = delayMilliseconds },
                ledger,
                TimeProvider.System),
            ledger);
    }

    private static AiTextGenerationRequest Request(
        string message,
        IReadOnlyList<GroundingItem>? grounding = null,
        IReadOnlyList<AiTurn>? earlier = null) =>
        AiTextGenerationRequest.Create(
            "Answer politely and briefly.",
            [.. earlier ?? [], AiTurn.FromUser(message)],
            grounding);

    [Fact]
    public async Task An_english_reply_starts_with_the_english_marker_and_quotes_the_message()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result =
            await generator.GenerateAsync(Request("When do you open on Fridays?"), Usage);

        Assert.StartsWith("Local test reply:", result.Text, StringComparison.Ordinal);
        Assert.Contains("not by an AI model", result.Text, StringComparison.Ordinal);
        Assert.Contains("\"When do you open on Fridays?\"", result.Text, StringComparison.Ordinal);
        Assert.Contains("No knowledge passage matched", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_arabic_message_gets_the_arabic_marker()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result =
            await generator.GenerateAsync(Request("متى تفتحون يوم الجمعة؟"), Usage);

        Assert.StartsWith("رد تجريبي محلي:", result.Text, StringComparison.Ordinal);
        Assert.Contains("«متى تفتحون يوم الجمعة؟»", result.Text, StringComparison.Ordinal);
        Assert.Contains("لا يوجد في المعرفة مقطع يطابق رسالتك.", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Local test reply", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_language_follows_the_latest_message_not_the_conversation()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result = await generator.GenerateAsync(
            Request("Thanks, and in English please?", earlier: [AiTurn.FromUser("مرحبا")]),
            Usage);

        Assert.StartsWith("Local test reply:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reply_names_the_passages_it_was_given_in_order()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result = await generator.GenerateAsync(
            Request(
                "How long do refunds take?",
                [
                    GroundingItem.Create("Returns policy", "Refunds reach your card within five working days."),
                    GroundingItem.Create("Shipping", "Orders ship the next working day."),
                ]),
            Usage);

        Assert.Contains("Knowledge passages that match your message (2):", result.Text, StringComparison.Ordinal);

        int returns = result.Text.IndexOf("1. Returns policy: \"Refunds reach", StringComparison.Ordinal);
        int shipping = result.Text.IndexOf("2. Shipping: \"Orders ship", StringComparison.Ordinal);

        Assert.True(returns > 0);
        Assert.True(shipping > returns);
    }

    [Fact]
    public async Task An_arabic_reply_names_its_passages_in_arabic()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result = await generator.GenerateAsync(
            Request(
                "كم يستغرق الاسترداد؟",
                [GroundingItem.Create("سياسة الإرجاع", "يصل المبلغ المسترد خلال خمسة أيام عمل.")]),
            Usage);

        Assert.Contains("مقاطع من المعرفة تطابق رسالتك (1):", result.Text, StringComparison.Ordinal);
        Assert.Contains("1. سياسة الإرجاع: «يصل المبلغ المسترد", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void At_most_three_passages_are_named_and_the_rest_are_counted()
    {
        GroundingItem[] five =
        [
            .. Enumerable.Range(1, 5).Select(index =>
                GroundingItem.Create($"Doc {index}", $"Passage number {index}.")),
        ];

        string reply = LocalStandInTextGenerator.Compose("Tell me everything", five);

        Assert.Contains("3. Doc 3:", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("Doc 4", reply, StringComparison.Ordinal);
        Assert.EndsWith("And 2 more.", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_request_always_gets_the_same_reply()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationRequest request = Request(
            "Is there parking?",
            [GroundingItem.Create("Visiting", "Free parking behind the building.")]);

        AiTextGenerationResult first = await generator.GenerateAsync(request, Usage);
        AiTextGenerationResult second = await generator.GenerateAsync(request, Usage);

        Assert.Equal(first.Text, second.Text);
    }

    [Theory]
    [InlineData("Please [[fail]] now")]
    [InlineData("[[FAIL]]")]
    [InlineData("اختبار [[fail]]")]
    public async Task The_fail_marker_fails_like_an_unavailable_provider(string message)
    {
        (LocalStandInTextGenerator generator, RecordingUsageLedger ledger) = Create();

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => generator.GenerateAsync(Request(message), Usage));

        AiProviderUsage row = ledger.Only;

        Assert.Equal(AiProvider.DevelopmentStandIn, row.Provider);
        Assert.Equal(AiOperationStatus.Failed, row.Status);
    }

    [Fact]
    public async Task The_fail_marker_counts_only_in_the_latest_message()
    {
        (LocalStandInTextGenerator generator, _) = Create();

        AiTextGenerationResult result = await generator.GenerateAsync(
            Request("And now?", earlier: [AiTurn.FromUser("[[fail]]")]),
            Usage);

        Assert.StartsWith("Local test reply:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_slow_marker_adds_ten_seconds_to_the_usual_delay()
    {
        (LocalStandInTextGenerator generator, _) = Create(delayMilliseconds: 1_000);

        Assert.Equal(TimeSpan.FromSeconds(1), generator.DelayFor("Hello"));
        Assert.Equal(TimeSpan.FromSeconds(11), generator.DelayFor("Hello [[slow]]"));
        Assert.Equal(TimeSpan.FromSeconds(11), generator.DelayFor("[[SLOW]]"));
    }

    [Fact]
    public async Task A_caller_who_gives_up_during_the_delay_is_not_kept_waiting()
    {
        (LocalStandInTextGenerator generator, RecordingUsageLedger ledger) = Create();

        using CancellationTokenSource cancelled = new(TimeSpan.FromMilliseconds(200));

        Task<AiTextGenerationResult> slow =
            generator.GenerateAsync(Request("Wait [[slow]]"), Usage, cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);

        Assert.Equal(AiOperationStatus.Failed, ledger.Only.Status);
    }

    [Fact]
    public async Task A_reply_is_accounted_at_zero_cost_with_word_counts()
    {
        (LocalStandInTextGenerator generator, RecordingUsageLedger ledger) = Create();

        AiTextGenerationResult result =
            await generator.GenerateAsync(Request("Two words"), Usage);

        AiProviderUsage row = ledger.Only;

        Assert.Equal(AiProvider.DevelopmentStandIn, row.Provider);
        Assert.Equal(AiOperationType.TextGeneration, row.OperationType);
        Assert.Equal(AiUsagePurpose.ConversationGeneration, row.Purpose);
        Assert.Equal(LocalAiStandInOptions.Model, row.RequestedModel.Value);
        Assert.Equal(AiOperationStatus.Succeeded, row.Status);
        Assert.Equal(0m, row.EstimatedCost);

        // Word counts, not tokens: the instructions (4) and the message (2) in,
        // the reply's own words out.
        Assert.Equal(6, row.Usage.InputTokens);
        Assert.Equal(LocalStandInText.CountWords(result.Text), row.Usage.OutputTokens);
    }
}
