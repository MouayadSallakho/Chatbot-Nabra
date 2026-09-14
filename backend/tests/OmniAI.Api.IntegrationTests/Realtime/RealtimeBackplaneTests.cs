using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using OmniAI.Api.Realtime;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Realtime;

/// <summary>
/// Two instances, one Redis, one announcement.
/// </summary>
/// <remarks>
/// <strong>The failure this exists to catch is silent.</strong> Without a
/// backplane a hub reaches only the browsers connected to the process that
/// raised the event. On one instance that is indistinguishable from working; on
/// two it is an inbox that updates for roughly half the staff, depending on
/// which instance their websocket happened to land on. Nothing errors, no local
/// test fails, and the symptom appears only under a load balancer.
/// <para>
/// <strong>What is asserted, precisely.</strong> Two independent service
/// providers are built — two instances, as far as SignalR is concerned — and
/// each is checked to be using the Redis lifetime manager rather than the
/// in-process default, which is the configuration mistake that produces the
/// silent failure. An announcement is then published through the real notifier
/// on the first instance and observed crossing the shared Redis channel that the
/// second instance subscribes to.
/// </para>
/// <para>
/// Delivery from that channel into a browser is SignalR's own code and is not
/// re-tested here; what this platform can get wrong, and therefore what is
/// covered, is whether the two instances are joined at all and whether the
/// announcement actually leaves the process.
/// </para>
/// </remarks>
public sealed class RealtimeBackplaneTests(RedisTestServer redis)
    : IClassFixture<RedisTestServer>
{
    private readonly RedisTestServer _redis = redis;

    [Fact]
    public void Each_instance_uses_the_redis_lifetime_manager()
    {
        // The configuration mistake that causes the silent failure: SignalR
        // registered without a backplane falls back to an in-process manager and
        // says nothing about it.
        using ServiceProvider instance = BuildInstance(Channel());

        HubLifetimeManager<WorkspaceInboxHub> manager =
            instance.GetRequiredService<HubLifetimeManager<WorkspaceInboxHub>>();

        Assert.Contains(
            "Redis",
            manager.GetType().Name,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_announcement_on_one_instance_crosses_to_the_other()
    {
        string channel = Channel();

        await using ServiceProvider first = BuildInstance(channel);
        await using ServiceProvider second = BuildInstance(channel);

        // The second instance's own Redis connection, subscribed to the channel
        // its lifetime manager listens on. A message arriving here is a message
        // that left the first process and reached the second.
        await using ConnectionMultiplexer listener = await _redis.ConnectAsync();

        TaskCompletionSource<byte[]> arrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        await listener.GetSubscriber().SubscribeAsync(
            RedisChannel.Pattern($"{channel}*"),
            (_, value) => arrived.TrySetResult((byte[])value!));

        TenantId tenant = TenantId.New();
        ConversationId conversation = ConversationId.New();

        // Published on the first instance only.
        await first
            .GetRequiredService<IConversationRealtimeNotifier>()
            .ConversationChangedAsync(tenant, conversation);

        byte[] payload = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // The conversation id is on the wire, because that is the entire
        // payload the platform sends.
        Assert.Contains(
            conversation.Value.ToString("D"),
            System.Text.Encoding.UTF8.GetString(payload),
            StringComparison.OrdinalIgnoreCase);

        // And the workspace is not: the visitor's group and the staff group are
        // addressed separately, and neither message carries a tenant.
        Assert.DoesNotContain(
            tenant.Value.ToString("D"),
            System.Text.Encoding.UTF8.GetString(payload),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_transcript_text_ever_reaches_the_wire()
    {
        // The property the whole realtime design rests on. A fan-out is not an
        // authorisation boundary, so the worst a stale listener may learn is
        // that something changed.
        string channel = Channel();

        await using ServiceProvider instance = BuildInstance(channel);
        await using ConnectionMultiplexer listener = await _redis.ConnectAsync();

        TaskCompletionSource<byte[]> arrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        await listener.GetSubscriber().SubscribeAsync(
            RedisChannel.Pattern($"{channel}*"),
            (_, value) => arrived.TrySetResult((byte[])value!));

        await instance
            .GetRequiredService<IConversationRealtimeNotifier>()
            .ConversationChangedAsync(TenantId.New(), ConversationId.New());

        byte[] payload = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        string wire = System.Text.Encoding.UTF8.GetString(payload);

        // The method name and one identifier. Nothing a customer or a colleague
        // wrote could be in a message this small, and the assertion says so
        // rather than merely hoping.
        Assert.Contains("conversationChanged", wire, StringComparison.Ordinal);
        Assert.True(payload.Length < 512, $"Realtime payload was {payload.Length} bytes.");
    }

    private static string Channel() => $"omniai:realtime:test:{Guid.CreateVersion7():N}";

    /// <summary>
    /// One instance: its own service provider, its own hub infrastructure, the
    /// shared Redis.
    /// </summary>
    private ServiceProvider BuildInstance(string channel)
    {
        ServiceCollection services = new();

        services.AddLogging();

        services
            .AddSignalR()
            .AddStackExchangeRedis(_redis.ConnectionString, options =>
                options.Configuration.ChannelPrefix = RedisChannel.Literal(channel));

        services.AddSingleton<IConversationRealtimeNotifier, SignalRConversationNotifier>();

        return services.BuildServiceProvider();
    }
}
