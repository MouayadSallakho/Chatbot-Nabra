using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations;

public sealed record ConversationNote(long Id, string? Author, DateTimeOffset CreatedAt, string Text);
public sealed record ConversationNotePage(IReadOnlyList<ConversationNote> Items, long? NextBefore);
public sealed record AddConversationNote(Guid RequestId, string? Text);

public interface IConversationNoteStore
{
    Task<ConversationNotePage?> ListAsync(TenantId tenant, ConversationId conversation, long? before, CancellationToken token);
    Task<ConversationNote?> AddAsync(TenantId tenant, ConversationId conversation, UserId author, Guid requestId,
        string text, DateTimeOffset now, CancellationToken token);
}

public sealed class ConversationNotesHandler(TenantAuthorizer authorizer, IConversationNoteStore store, TimeProvider clock)
{
    public const int MaxLength = 2000;

    public async Task<ConversationNotePage?> ListAsync(Guid tenantId, Guid conversationId, long? before, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsView, token).ConfigureAwait(false))
        {
            return null;
        }

        return await store.ListAsync(tenant, ConversationId.From(conversationId), before, token).ConfigureAwait(false);
    }

    public async Task<ConversationNote?> AddAsync(Guid tenantId, Guid conversationId, AddConversationNote request, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsView, token).ConfigureAwait(false) ||
            !await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsReply, token).ConfigureAwait(false))
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(request);
        string text = request.Text?.Trim() ?? string.Empty;
        if (request.RequestId == Guid.Empty || text.Length is 0 or > MaxLength || text.Contains('\0'))
        {
            throw new DomainValidationException("Provide a request ID and a note of 1 to 2000 characters.")
            { Code = "conversations.note.invalid" };
        }

        var note = await store.AddAsync(tenant, ConversationId.From(conversationId), authorizer.RequireCurrentUser(),
            request.RequestId, text, clock.GetUtcNow(), token).ConfigureAwait(false);
        if (note is not null && !string.Equals(note.Text, text, StringComparison.Ordinal))
        {
            throw new DomainValidationException("This request ID was already used for a different note.")
            { Code = "conversations.note.request_reused" };
        }
        return note;
    }
}
