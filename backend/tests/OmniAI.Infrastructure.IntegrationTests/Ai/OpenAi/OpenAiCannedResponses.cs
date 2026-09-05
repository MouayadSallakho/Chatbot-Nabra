using System.Globalization;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// Response bodies in the shape the OpenAI Responses API actually returns.
/// </summary>
/// <remarks>
/// Written as raw JSON rather than built from SDK model types, deliberately. The
/// point of these tests is that the adapter copes with what the provider sends,
/// so the fixture has to be the wire format — building it from the same objects
/// the SDK deserialises into would make the test agree with itself and prove
/// nothing about deserialisation.
/// <para>
/// The shapes here were taken from a real round-trip through the installed SDK
/// rather than from memory of the documentation.
/// </para>
/// </remarks>
internal static class OpenAiCannedResponses
{
    /// <summary>A completed response carrying one assistant message.</summary>
    public static string Completed(string text) => $$"""
    {
      "id": "resp_canned_1",
      "object": "response",
      "created_at": 1730000000,
      "status": "completed",
      "model": "gpt-canned",
      "output": [
        { "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
          "content": [ { "type": "output_text", "text": {{Quote(text)}}, "annotations": [] } ] }
      ],
      "usage": { "input_tokens": 11, "output_tokens": 4, "total_tokens": 15,
                 "input_tokens_details": { "cached_tokens": 0 },
                 "output_tokens_details": { "reasoning_tokens": 0 } }
    }
    """;

    /// <summary>A completed response carrying two output text parts.</summary>
    /// <remarks>
    /// The Responses API can return more than one text part. This exists so the
    /// adapter's handling of that case is asserted against real SDK semantics
    /// rather than assumed.
    /// </remarks>
    public static string CompletedInTwoParts(string first, string second) => $$"""
    {
      "id": "resp_canned_2",
      "object": "response",
      "created_at": 1730000000,
      "status": "completed",
      "model": "gpt-canned",
      "output": [
        { "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
          "content": [
            { "type": "output_text", "text": {{Quote(first)}}, "annotations": [] },
            { "type": "output_text", "text": {{Quote(second)}}, "annotations": [] }
          ] }
      ],
      "usage": { "input_tokens": 11, "output_tokens": 8, "total_tokens": 19 }
    }
    """;

    /// <summary>A completed response whose output list is empty.</summary>
    public static string NoOutput() => """
    {
      "id": "resp_canned_3",
      "object": "response",
      "created_at": 1730000000,
      "status": "completed",
      "model": "gpt-canned",
      "output": [],
      "usage": { "input_tokens": 11, "output_tokens": 0, "total_tokens": 11 }
    }
    """;

    /// <summary>An OpenAI error body, as the API returns for a failed call.</summary>
    /// <remarks>
    /// Carries text that must never reach a client — a vendor name, a model name
    /// and a request id — so the leak assertions have something real to catch.
    /// </remarks>
    public static string Error(string code, string message) => $$"""
    {
      "error": {
        "type": "invalid_request_error",
        "code": {{Quote(code)}},
        "message": {{Quote(message)}},
        "param": null
      },
      "request_id": "req_canned_leak_probe"
    }
    """;

    private static string Quote(string value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal)
                      .Replace("\"", "\\\"", StringComparison.Ordinal)
                      .Replace("\n", "\\n", StringComparison.Ordinal)
                      .Replace("\r", "\\r", StringComparison.Ordinal)
                      .Replace("\t", "\\t", StringComparison.Ordinal)}\"");
}
