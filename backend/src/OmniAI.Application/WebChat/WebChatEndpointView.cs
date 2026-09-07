using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat;

/// <summary>
/// One website chat endpoint, as the admin API publishes it.
/// </summary>
/// <remarks>
/// <strong>The public key is here on purpose.</strong> It is the whole point of
/// the screen — an administrator has to be able to copy the chat link — and it is
/// not a secret. Nothing else that could be mistaken for one appears: there is no
/// access token, no session, no visitor and no transcript.
/// </remarks>
public sealed record WebChatEndpointView(
    Guid Id,
    Guid AiAgentId,
    string AiAgentName,
    string AiAgentStatus,
    string PublicKey,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Builds a view from a stored row.</summary>
    public static WebChatEndpointView From(WebChatEndpointRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new WebChatEndpointView(
            row.Id,
            row.AiAgentId,
            row.AiAgentName,
            row.AiAgentStatus.ToString(),
            row.PublicKey,
            row.Status.ToString(),
            row.CreatedAt,
            row.UpdatedAt);
    }

    /// <summary>Builds a view from the aggregate and the agent it names.</summary>
    public static WebChatEndpointView From(
        WebChatEndpoint endpoint,
        string agentName,
        AiAgentStatus agentStatus)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new WebChatEndpointView(
            endpoint.Id.Value,
            endpoint.AiAgentId.Value,
            agentName,
            agentStatus.ToString(),
            endpoint.PublicKey.Value,
            endpoint.Status.ToString(),
            endpoint.CreatedAt,
            endpoint.UpdatedAt);
    }
}
