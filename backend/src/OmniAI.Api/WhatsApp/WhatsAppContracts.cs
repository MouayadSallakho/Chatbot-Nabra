using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;

namespace OmniAI.Api.WhatsApp;

/// <summary>Connect a WhatsApp number to one of the workspace's agents.</summary>
/// <param name="DisplayPhoneNumber">
/// The number as a person would recognise it. Shown in the admin so somebody can
/// tell two connections apart; it is never used for routing.
/// </param>
public sealed record CreateWhatsAppConnectionRequest(
    Guid? AiAgentId,
    string? WabaId,
    string? PhoneNumberId,
    string? DisplayPhoneNumber);

/// <summary>Change which number and agent a connection uses.</summary>
public sealed record UpdateWhatsAppConnectionRequest(
    Guid? AiAgentId,
    string? WabaId,
    string? PhoneNumberId,
    string? DisplayPhoneNumber);

/// <summary>
/// Store a new access token for a connection.
/// </summary>
/// <remarks>
/// <strong>Write-only, and there is no response that echoes it.</strong> The
/// token arrives here, is encrypted and is stored. No endpoint returns it, and
/// the admin is told only whether one is configured.
/// </remarks>
public sealed record ReplaceWhatsAppCredentialRequest(string? AccessToken);

/// <summary>
/// One WhatsApp connection, on the wire.
/// </summary>
/// <remarks>
/// <strong>There is no access-token field, and there is nowhere one could be
/// put.</strong> <see cref="HasCredential"/> is a boolean saying whether a token
/// is stored; that is the whole of what an administrator is told about it. The
/// aggregate behind this response does not hold the token either, so this is a
/// property of the design rather than a rule somebody has to remember when adding
/// the next field.
/// </remarks>
public sealed record WhatsAppConnectionResponse(
    Guid Id,
    Guid AiAgentId,
    string? AiAgentName,
    string WabaId,
    string PhoneNumberId,
    string? DisplayPhoneNumber,
    string Status,
    bool HasCredential,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>From a list row, which carries the agent's name.</summary>
    public static WhatsAppConnectionResponse From(WhatsAppConnectionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new WhatsAppConnectionResponse(
            row.Id,
            row.AiAgentId,
            row.AiAgentName,
            row.WabaId,
            row.PhoneNumberId,
            row.DisplayPhoneNumber,
            row.Status.ToString(),
            row.HasCredential,
            row.CreatedAt,
            row.UpdatedAt);
    }

    /// <summary>From the view a single-connection read or write returns.</summary>
    public static WhatsAppConnectionResponse From(WhatsAppConnectionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new WhatsAppConnectionResponse(
            view.Id,
            view.AiAgentId,
            view.AiAgentName,
            view.WabaId,
            view.PhoneNumberId,
            view.DisplayPhoneNumber,
            view.Status.ToString(),
            view.HasCredential,
            view.CreatedAt,
            view.UpdatedAt);
    }
}

/// <summary>
/// The WhatsApp side of one conversation, on the wire.
/// </summary>
/// <remarks>
/// <strong>The customer's number leaves masked.</strong> A colleague needs to
/// recognise who they are talking to; nobody needs an exportable contact list,
/// and a browser tab is not a place to keep one.
/// </remarks>
public sealed record WhatsAppConversationResponse(
    string CustomerReference,
    IReadOnlyDictionary<string, string> DeliveryStatuses)
{
    public static WhatsAppConversationResponse From(WhatsAppConversationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return new WhatsAppConversationResponse(
            facts.CustomerReference,
            facts.DeliveryStatuses.ToDictionary(
                entry => entry.Key.ToString(),
                entry => entry.Value.ToString()));
    }
}
