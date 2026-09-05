using OmniAI.Application.Ai;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// What the adapter makes of what the provider returns.
/// </summary>
/// <remarks>
/// Canned bodies in the real wire format go in; only this platform's own types
/// come out. Nothing here needs a network or an account.
/// </remarks>
public sealed class OpenAiResponseTests
{
    [Fact]
    public async Task Generated_text_becomes_a_result()
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.Completed("A student laptop should be light."),
        };

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal("A student laptop should be light.", result.Text);
    }

    [Fact]
    public async Task Interior_formatting_of_the_reply_is_preserved()
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.Completed("Two options:\n\n- A\n- B"),
        };

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal("Two options:\n\n- A\n- B", result.Text);
    }

    [Fact]
    public async Task Multiple_output_text_parts_are_handled_as_the_sdk_defines()
    {
        // The Responses API may return more than one text part. This asserts the
        // installed SDK's own GetOutputText semantics rather than a guess: both
        // parts must be represented, in order, and neither may be silently
        // dropped.
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.CompletedInTwoParts("First part. ", "Second part."),
        };

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Contains("First part.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Second part.", result.Text, StringComparison.Ordinal);
        Assert.True(
            result.Text.IndexOf("First part.", StringComparison.Ordinal)
                < result.Text.IndexOf("Second part.", StringComparison.Ordinal),
            "The output parts were reordered.");
    }

    [Fact]
    public async Task A_response_with_no_output_is_refused()
    {
        // An empty answer must never reach a person as though the agent had
        // chosen to say nothing.
        CannedOpenAiTransport transport = new() { ResponseBody = OpenAiCannedResponses.NoOutput() };

        await Assert.ThrowsAsync<AiResponseInvalidException>(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public async Task A_blank_generated_reply_is_refused(string text)
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.Completed(text),
        };

        await Assert.ThrowsAsync<AiResponseInvalidException>(
            () => OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest()));
    }

    [Fact]
    public async Task A_reply_is_trimmed_at_the_ends()
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.Completed("  Certainly.\n"),
        };

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal("Certainly.", result.Text);
    }

    [Fact]
    public async Task Only_this_platforms_own_type_comes_back()
    {
        // No ResponseResult, no ClientResult, no provider model of any kind. The
        // adapter's return type is the contract, and the assembly it lives in is
        // the proof that no vendor type escaped.
        CannedOpenAiTransport transport = new();

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal("OmniAI.Application", result.GetType().Assembly.GetName().Name);
        Assert.Equal(typeof(AiTextGenerationResult), result.GetType());
    }

    [Fact]
    public async Task No_provider_identifier_or_usage_reaches_the_caller()
    {
        // The response body carries a response id and a token count. Neither is
        // part of the result contract yet, so neither may appear in what comes
        // back.
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        AiTextGenerationResult result = await OpenAiTestHarness.Generator(transport)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal("Text", Assert.Single(
            result.GetType().GetProperties()
                .Where(property => property.Name != "EqualityContract")
                .Select(property => property.Name)));

        Assert.DoesNotContain("resp_canned", result.Text, StringComparison.Ordinal);
    }
}
