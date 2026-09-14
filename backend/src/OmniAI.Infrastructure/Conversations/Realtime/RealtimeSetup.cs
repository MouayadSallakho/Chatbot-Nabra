using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;
using OmniAI.Application.WebChat.Protection;

namespace OmniAI.Infrastructure.Conversations.Realtime;

/// <summary>
/// Registers the store behind public realtime tickets.
/// </summary>
/// <remarks>
/// <strong>It reuses the Redis the public-chat protection already
/// configures.</strong> A second connection string would be a second thing to
/// run, a second thing to get wrong, and a second thing to explain — for a
/// workload that needs no isolation from the counters. The key spaces do not
/// overlap, and the two uses have opposite failure requirements only in the sense
/// that matters here: the counters are authoritative and fail closed, while
/// realtime is best effort. Both are served by the same client.
/// </remarks>
public static class RealtimeSetup
{
    /// <summary>Adds the realtime ticket store, or the refusal that stands in for it.</summary>
    public static IServiceCollection AddOmniAiRealtimeTickets(
        this IServiceCollection services,
        string? redisConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // No store, so no ticket can be minted or checked, so no public
            // listener is admitted. The rest of the platform is unaffected: a
            // visitor still sends and reads messages, they simply have to reload
            // to see a reply. Degrading to "admit everybody" instead would trade
            // an inconvenience for a disclosure.
            services.AddSingleton<IRealtimeTickets, UnavailableRealtimeTickets>();

            return services;
        }

        services.AddSingleton<IRealtimeTickets>(provider => new RedisRealtimeTickets(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<PublicChatProtectionOptions>().KeyPrefix + ":"));

        return services;
    }
}

/// <summary>
/// The realtime ticket store on a deployment that has none.
/// </summary>
/// <remarks>
/// Refuses rather than pretending. Minting throws, because a caller asking for a
/// ticket has already been authorised and deserves to know the platform cannot
/// give them one; redeeming answers "no such ticket", because that is true and
/// because a hub is not a place to raise configuration errors at strangers.
/// </remarks>
internal sealed class UnavailableRealtimeTickets : IRealtimeTickets
{
    public Task<RealtimeTicket> IssueAsync(
        ConversationId conversationId,
        WebChatClientMode mode,
        CancellationToken cancellationToken = default) =>
        throw new RealtimeUnavailableException();

    public Task<RealtimeTicketClaim?> RedeemAsync(
        string? ticket,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<RealtimeTicketClaim?>(null);
}
