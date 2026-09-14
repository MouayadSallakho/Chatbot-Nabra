using OmniAI.Application.Authorization;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>What the inbox needs to know about a WhatsApp conversation.</summary>
public sealed record ReadWhatsAppConversationQuery(Guid TenantId, Guid ConversationId);

/// <summary>
/// The WhatsApp-specific facts about one conversation.
/// </summary>
/// <param name="CustomerReference">
/// The customer's number, masked to its last four digits. Enough for a colleague
/// to recognise who they are talking to and not enough to be a contact list.
/// </param>
/// <param name="DeliveryStatuses">
/// The delivery state of each outbound message, keyed by message id. Absent for
/// a message with no delivery — a customer's own message, or a reply written
/// before the conversation was on WhatsApp.
/// </param>
public sealed record WhatsAppConversationFacts(
    string CustomerReference,
    IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus> DeliveryStatuses);

/// <summary>
/// Reading the WhatsApp side of a conversation, for the shared inbox.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A separate read rather than fields on the conversation.</strong> The
/// inbox is one screen for every channel, and the conversation reader is
/// deliberately free of anything channel-specific: adding a WhatsApp column to
/// it would be the first of four, and each would be loaded for every
/// conversation in every list regardless of where it came from.
/// </para>
/// <para>
/// <strong>Guarded by <c>conversations.view</c>, not by a WhatsApp
/// permission.</strong> This is conversation content — who the customer is and
/// whether they received a reply — and it must be readable by exactly the people
/// who may read the transcript. Somebody who administers the channel but may not
/// read conversations gets nothing here.
/// </para>
/// <para>
/// <strong>The number leaves masked and is masked in the Domain.</strong> The
/// value object's own <c>ToString</c> returns the masked form, so a future caller
/// who interpolates it into a log or a response discloses four digits rather than
/// a phone number.
/// </para>
/// </remarks>
public sealed class ReadWhatsAppConversationHandler(
    TenantAuthorizer authorizer,
    IWhatsAppContactStore contacts,
    IWhatsAppOutbox outbox)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IWhatsAppContactStore _contacts =
        contacts ?? throw new ArgumentNullException(nameof(contacts));

    private readonly IWhatsAppOutbox _outbox =
        outbox ?? throw new ArgumentNullException(nameof(outbox));

    /// <returns>
    /// The facts, or <see langword="null"/> when the caller is not entitled or
    /// the conversation did not come from WhatsApp.
    /// </returns>
    public async Task<WhatsAppConversationFacts?> HandleAsync(
        ReadWhatsAppConversationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        ConversationId conversationId = ConversationId.From(query.ConversationId);

        WhatsAppConversationLink? link = await _contacts
            .FindLinkAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        // The workspace is compared rather than assumed. A conversation id from
        // another workspace finds a link that does not belong to this caller, and
        // the answer is the same absence they would get for one that never
        // existed.
        if (link is null || link.TenantId != tenantId)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus> statuses = await _outbox
            .ListStatusesAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        return new WhatsAppConversationFacts(link.CustomerId.Masked, statuses);
    }
}
