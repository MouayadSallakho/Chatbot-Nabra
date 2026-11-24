using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

public sealed record QuickReply(Guid Id, string Text);

public interface IQuickReplyStore
{
    Task<IReadOnlyList<QuickReply>> ListAsync(TenantId tenant, string language, CancellationToken token);
    Task AddAsync(TenantId tenant, string language, string text, CancellationToken token);
    Task DeleteAsync(TenantId tenant, string language, Guid id, CancellationToken token);
}

public sealed class QuickReplyHandler(TenantAuthorizer authorizer, IQuickReplyStore store)
{
    public async Task<IReadOnlyList<QuickReply>?> ListAsync(Guid tenantId, string language, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsReply, token).ConfigureAwait(false))
        {
            return null;
        }

        return await store.ListAsync(tenant, Language(language), token).ConfigureAwait(false);
    }

    public async Task<bool> AddAsync(Guid tenantId, string language, string? text, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await authorizer.RequireAsync(tenant, PlatformPermissions.WorkspaceManage, token).ConfigureAwait(false))
        {
            return false;
        }

        string value = text?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 500 || value.Contains('\0'))
        {
            throw new DomainValidationException("A quick reply must contain 1 to 500 characters of plain text.")
            { Code = "conversations.quick_reply.text_invalid", Field = "text" };
        }
        await store.AddAsync(tenant, Language(language), value, token).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid tenantId, string language, Guid id, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await authorizer.RequireAsync(tenant, PlatformPermissions.WorkspaceManage, token).ConfigureAwait(false))
        {
            return false;
        }

        await store.DeleteAsync(tenant, Language(language), id, token).ConfigureAwait(false);
        return true;
    }

    private static string Language(string language) => language is "en" or "ar" ? language
        : throw new DomainValidationException("Choose English or Arabic for quick replies.")
        { Code = "conversations.quick_reply.language_invalid", Field = "language" };
}
