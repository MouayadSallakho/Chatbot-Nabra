using OmniAI.Application.Authorization;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

public sealed record RoutingRule(string Channel, string Language, Guid RoleId);
public sealed record RoutingTeam(Guid Id, string Name);
public sealed record RoutingSettings(IReadOnlyList<RoutingRule> Rules, IReadOnlyList<RoutingTeam> Teams, RoutingHours Hours, int FirstReplyTargetMinutes);

public interface IInboxRoutingStore
{
    Task<RoutingSettings> ReadAsync(TenantId tenant, CancellationToken token);
    Task<bool> SetAsync(TenantId tenant, ChannelKind channel, string language, Guid? role, CancellationToken token);
    Task SetTargetAsync(TenantId tenant, int minutes, CancellationToken token);
    Task<bool> SetHoursAsync(TenantId tenant, RoutingHours hours, CancellationToken token);
}

public sealed class InboxRoutingHandler(TenantAuthorizer authorizer, IInboxRoutingStore store)
{
    private async Task<bool> CanManageAsync(TenantId tenant, CancellationToken token) =>
        await authorizer.RequireAsync(tenant, PlatformPermissions.WorkspaceManage, token).ConfigureAwait(false) &&
        await authorizer.RequireAsync(tenant, PlatformPermissions.ConversationsAssign, token).ConfigureAwait(false);

    public async Task<RoutingSettings?> ReadAsync(Guid tenantId, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        return await CanManageAsync(tenant, token).ConfigureAwait(false)
            ? await store.ReadAsync(tenant, token).ConfigureAwait(false) : null;
    }

    public async Task<bool> SetTargetAsync(Guid tenantId, int minutes, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanManageAsync(tenant, token).ConfigureAwait(false))
        {
            return false;
        }
        if (minutes is < 1 or > 1440)
        {
            throw new DomainValidationException("Choose a first reply target between 1 and 1440 minutes.")
            { Code = "conversations.routing.target_invalid" };
        }
        await store.SetTargetAsync(tenant, minutes, token).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetHoursAsync(Guid tenantId, RoutingHours hours, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanManageAsync(tenant, token).ConfigureAwait(false))
        {
            return false;
        }
        ArgumentNullException.ThrowIfNull(hours);
        hours.Validate();
        return await store.SetHoursAsync(tenant, hours, token).ConfigureAwait(false);
    }

    public async Task<bool> SetAsync(Guid tenantId, string channel, string language, Guid? role, CancellationToken token)
    {
        var tenant = TenantId.From(tenantId);
        if (!await CanManageAsync(tenant, token).ConfigureAwait(false))
        {
            return false;
        }
        if (!Enum.TryParse<ChannelKind>(channel, out var kind) || !Enum.IsDefined(kind) || kind.ToString() != channel ||
            language is not ("ar" or "en") || role == Guid.Empty)
        {
            throw new DomainValidationException("Select a supported channel, language and workspace role.")
            { Code = "conversations.routing.invalid" };
        }
        return await store.SetAsync(tenant, kind, language, role, token).ConfigureAwait(false);
    }
}
