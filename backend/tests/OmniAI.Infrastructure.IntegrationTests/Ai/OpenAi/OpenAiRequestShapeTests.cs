using System.Text.Json;
using OmniAI.Application.Ai;
using OmniAI.Infrastructure.Ai.OpenAi;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// What the adapter actually puts on the wire.
/// </summary>
/// <remarks>
/// Assertions are made against the serialised request body the real SDK produced,
/// captured at the transport. That is the only place where "instructions are not
/// concatenated" and "no tool is enabled" can be proven rather than assumed —
/// checking the options object would only confirm what this code set, not what
/// OpenAI would receive.
/// </remarks>
public sealed class OpenAiRequestShapeTests
{
    /// <summary>The roles an input item may carry. There are no others.</summary>
    private static readonly string[] AllowedRoles = ["user", "assistant"];

    /// <summary>The four turns of the ordering check, and their roles.</summary>
    private static readonly string[] OrderedRoles = ["user", "assistant", "user", "assistant"];

    private static readonly string[] OrderedTexts = ["First", "Second", "Third", "Fourth"];

    [Fact]
    public async Task The_configured_model_is_sent()
    {
        JsonElement body = await SendAsync(
            OpenAiTestHarness.SimpleRequest(),
            new OpenAiOptions { Model = "gpt-configured-for-this-test" });

        Assert.Equal("gpt-configured-for-this-test", body.GetProperty("model").GetString());
    }

    [Fact]
    public async Task The_default_model_is_the_documented_one()
    {
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal(OpenAiOptions.DefaultModel, body.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Instructions_are_sent_in_the_providers_instruction_field()
    {
        JsonElement body = await SendAsync(
            AiTextGenerationRequest.Create(
                "Never quote a discount.",
                [AiTurn.FromUser("Any discount?")]));

        Assert.Equal("Never quote a discount.", body.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Instructions_are_never_concatenated_into_the_input()
    {
        // The privilege boundary, checked on the wire. If the adapter ever
        // flattened these two, the instruction text would appear inside an input
        // item and this fails.
        JsonElement body = await SendAsync(
            AiTextGenerationRequest.Create(
                "Never quote a discount.",
                [AiTurn.FromUser("Any discount?")]));

        Assert.DoesNotContain(
            "Never quote a discount",
            body.GetProperty("input").GetRawText(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Any discount?",
            body.GetProperty("instructions").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_turn_becomes_a_user_item()
    {
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest(message: "Which laptop?"));

        JsonElement item = body.GetProperty("input")[0];

        Assert.Equal("message", item.GetProperty("type").GetString());
        Assert.Equal("user", item.GetProperty("role").GetString());
        Assert.Contains("Which laptop?", item.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assistant_turn_becomes_an_assistant_item()
    {
        JsonElement body = await SendAsync(
            AiTextGenerationRequest.Create(
                "Be helpful.",
                [AiTurn.FromUser("Hello?"), AiTurn.FromAssistant("Hello. How can I help?")]));

        Assert.Equal("assistant", body.GetProperty("input")[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Turn_order_is_preserved_exactly()
    {
        JsonElement body = await SendAsync(
            AiTextGenerationRequest.Create(
                "Be helpful.",
                [
                    AiTurn.FromUser("First"),
                    AiTurn.FromAssistant("Second"),
                    AiTurn.FromUser("Third"),
                    AiTurn.FromAssistant("Fourth"),
                ]));

        JsonElement input = body.GetProperty("input");

        Assert.Equal(4, input.GetArrayLength());
        Assert.Equal(
            OrderedRoles,
            input.EnumerateArray().Select(item => item.GetProperty("role").GetString()));

        Assert.Equal(
            OrderedTexts,
            input.EnumerateArray().Select(item => item.GetProperty("content")[0].GetProperty("text").GetString()));
    }

    [Fact]
    public async Task No_system_or_developer_item_is_ever_produced()
    {
        // The Application has no such role, and the adapter offers no path to the
        // SDK factories that would create one. Checked on the wire because that is
        // where it would show.
        JsonElement body = await SendAsync(
            AiTextGenerationRequest.Create(
                "Be helpful.",
                [AiTurn.FromUser("Hello?"), AiTurn.FromAssistant("Hi.")]));

        foreach (JsonElement item in body.GetProperty("input").EnumerateArray())
        {
            string role = item.GetProperty("role").GetString()!;

            Assert.Contains(role, AllowedRoles, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Provider_side_storage_is_disabled()
    {
        // store = false. Preview is stateless, and a platform that keeps nothing
        // itself should not leave retrievable copies with a third party.
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.False(body.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task The_configured_output_bound_is_sent()
    {
        JsonElement body = await SendAsync(
            OpenAiTestHarness.SimpleRequest(),
            new OpenAiOptions { MaxOutputTokens = 321 });

        Assert.Equal(321, body.GetProperty("max_output_tokens").GetInt32());
    }

    [Fact]
    public async Task The_default_output_bound_is_the_documented_one()
    {
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal(
            OpenAiOptions.DefaultMaxOutputTokens,
            body.GetProperty("max_output_tokens").GetInt32());
    }

    [Fact]
    public async Task Streaming_is_not_requested()
    {
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.False(body.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task No_tool_of_any_kind_is_enabled()
    {
        // Phase 9 is text generation only: no web search, file search, computer
        // use, functions, MCP or code interpreter.
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        if (body.TryGetProperty("tools", out JsonElement tools))
        {
            Assert.Equal(0, tools.GetArrayLength());
        }

        foreach (string forbidden in (string[])
            ["web_search", "file_search", "computer_use", "code_interpreter",
             "function", "mcp", "tool_choice"])
        {
            Assert.DoesNotContain(forbidden, body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task No_conversation_chaining_is_requested()
    {
        // previous_response_id and conversation would both make the provider
        // maintain history the platform has not asked for and does not want.
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.False(body.TryGetProperty("previous_response_id", out _));
        Assert.False(body.TryGetProperty("conversation", out _));
    }

    [Fact]
    public async Task No_end_user_identifier_or_safety_identifier_is_sent()
    {
        // End-user tracking is not authorised, and sending an identifier the
        // platform never decided to send would be a privacy decision made by
        // accident.
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.False(body.TryGetProperty("user", out _));
        Assert.False(body.TryGetProperty("safety_identifier", out _));
        Assert.False(body.TryGetProperty("prompt_cache_key", out _));
        Assert.False(body.TryGetProperty("metadata", out _));
    }

    [Fact]
    public async Task No_sampling_parameters_are_invented()
    {
        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest());

        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.False(body.TryGetProperty("top_p", out _));
    }

    [Fact]
    public async Task The_credential_travels_in_the_header_and_never_in_the_body()
    {
        CannedOpenAiTransport transport = new();

        await OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.True(transport.SentAuthorizationHeader, "The request carried no Authorization header.");

        // Presence is asserted; the value is never read. The body must not carry
        // it under any name.
        Assert.DoesNotContain(
            OpenAiTestHarness.DummyApiKey, transport.LastBody, StringComparison.Ordinal);

        foreach (string forbidden in (string[])["api_key", "apiKey", "authorization", "bearer"])
        {
            Assert.DoesNotContain(forbidden, transport.LastBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_request_carries_no_platform_metadata()
    {
        // No tenant id, no agent id, no internal identifier. The provider is asked
        // to generate text; who it is for is this platform's business.
        CannedOpenAiTransport transport = new();

        await OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest());

        foreach (string forbidden in (string[])
            ["tenant", "agent_id", "aiAgentId", "omniai", "xmin", "postgres"])
        {
            Assert.DoesNotContain(forbidden, transport.LastBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Exactly_one_http_call_is_made_for_one_generation()
    {
        CannedOpenAiTransport transport = new();

        await OpenAiTestHarness.Generator(transport).GenerateAsync(OpenAiTestHarness.SimpleRequest());

        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Multiline_message_text_survives_serialisation()
    {
        const string Multiline = "I need:\n\n  1. a laptop\n  2. a case\n\nWhich first?";

        JsonElement body = await SendAsync(OpenAiTestHarness.SimpleRequest(message: Multiline));

        Assert.Equal(
            Multiline,
            body.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    // ------------------------------------------------------------------- helpers

    /// <summary>Sends a request and returns the JSON body the SDK produced.</summary>
    private static async Task<JsonElement> SendAsync(
        AiTextGenerationRequest request,
        OpenAiOptions? options = null)
    {
        CannedOpenAiTransport transport = new();

        await OpenAiTestHarness.Generator(transport, options).GenerateAsync(request);

        return JsonDocument.Parse(transport.LastBody).RootElement.Clone();
    }
}
