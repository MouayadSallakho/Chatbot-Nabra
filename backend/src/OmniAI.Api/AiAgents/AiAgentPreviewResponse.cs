using OmniAI.Application.AiAgents.PreviewAiAgent;

namespace OmniAI.Api.AiAgents;

/// <summary>
/// The body of a successful preview response.
/// </summary>
/// <param name="Reply">The agent's generated answer.</param>
/// <remarks>
/// <para>
/// One field, and the list of what it does not carry is the design: no
/// <c>instructions</c>, no <c>prompt</c>, no <c>model</c>, no <c>provider</c>, no
/// token counts, no cost, no finish reason, no latency, no provider request id and
/// no raw upstream payload.
/// </para>
/// <para>
/// The instructions are omitted deliberately rather than for brevity. They are
/// business configuration that can describe internal policy, and a reply body is
/// the most quotable, most forwarded, most logged part of any response -- so the
/// place they must not be is here. A caller entitled to them can read them from
/// the agent endpoint, where their appearance is the point rather than a side
/// effect.
/// </para>
/// <para>
/// The rest are absent because they do not exist yet: the Application's result
/// carries only text, and publishing a field the platform cannot fill would be a
/// promise this API could not keep. When a real provider shows which of them are
/// available and stable, they can be added -- an addition clients tolerate,
/// unlike the removal that guessing now would eventually force.
/// </para>
/// <para>
/// A preview response carries no <c>ETag</c> either. Nothing was read whose
/// version a client could later present, and nothing was changed -- the reply is
/// generated, not stored, so there is no resource here to be out of date about.
/// </para>
/// </remarks>
public sealed record AiAgentPreviewResponse(string Reply)
{
    /// <summary>Maps an Application preview onto the HTTP contract.</summary>
    internal static AiAgentPreviewResponse From(AiAgentPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new AiAgentPreviewResponse(preview.Reply);
    }
}
