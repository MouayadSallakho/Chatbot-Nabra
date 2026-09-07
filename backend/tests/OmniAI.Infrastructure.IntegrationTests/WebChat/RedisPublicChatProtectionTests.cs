using StackExchange.Redis;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.WebChat.Protection;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.WebChat;

/// <summary>
/// The distributed limiter, measured against a real Redis.
/// </summary>
/// <remarks>
/// <strong>Real Redis, and it is not negotiable.</strong> Every property worth
/// asserting here is Redis-specific: that a Lua script evaluates atomically, that
/// <c>INCR</c> plus <c>EXPIRE</c> produces an anchored window, that <c>PTTL</c>
/// gives an honest retry hint, that two clients sharing a server see one another's
/// counts, and that keys really do vanish. An in-memory fake would implement
/// whatever this platform assumed and would pass while the assumption was wrong,
/// which for a distributed limiter is the only failure that matters.
/// <para>
/// Each test uses tiny limits and short windows supplied as ordinary options, so
/// what is measured is the real production adapter with a real production policy
/// object — not a special mode.
/// </para>
/// </remarks>
public sealed class RedisPublicChatProtectionTests(RedisTestServer redis)
    : IClassFixture<RedisTestServer>, IAsyncLifetime
{
    private static readonly TenantId Workspace = TenantId.New();

    public Task InitializeAsync() => redis.ClearAsync();

    public Task DisposeAsync() => redis.ClearAsync();

    // ---- The basic contract ----------------------------------------------

    [Fact]
    public async Task The_first_request_is_allowed()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, requests: 3);

        Assert.True((await protection.AuthoriseRequestAsync(Network("a"))).Allowed);
    }

    [Fact]
    public async Task Requests_are_allowed_up_to_the_limit_and_refused_after_it()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, requests: 3);
        ClientNetworkKey network = Network("threshold");

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(
                (await protection.AuthoriseRequestAsync(network)).Allowed,
                $"Attempt {attempt} of 3 was refused.");
        }

        PublicChatDecision refused = await protection.AuthoriseRequestAsync(network);

        Assert.False(refused.Allowed);
    }

    [Fact]
    public async Task A_refusal_carries_a_retry_hint_no_longer_than_the_window()
    {
        // The remaining life of the counter that refused -- the shortest honest
        // answer to "when can I try again", rather than a guess or a constant.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            requests: 1,
            window: TimeSpan.FromMinutes(5));

        ClientNetworkKey network = Network("retry-after");

        await protection.AuthoriseRequestAsync(network);

        PublicChatDecision refused = await protection.AuthoriseRequestAsync(network);

        Assert.False(refused.Allowed);
        Assert.True(refused.RetryAfter > TimeSpan.Zero, "The retry hint was not positive.");
        Assert.True(
            refused.RetryAfter <= TimeSpan.FromMinutes(5),
            $"The retry hint {refused.RetryAfter} exceeded the window.");
    }

    [Fact]
    public async Task An_allowed_decision_carries_no_retry_hint()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        PublicChatDecision allowed = await Build(client, requests: 5)
            .AuthoriseRequestAsync(Network("no-hint"));

        Assert.True(allowed.Allowed);
        Assert.Equal(TimeSpan.Zero, allowed.RetryAfter);
    }

    // ---- TTLs and cleanup -------------------------------------------------

    [Fact]
    public async Task Every_counter_carries_a_bounded_ttl()
    {
        // Redis is protection state, not storage. A key with no expiry would be a
        // permanent record of somebody's network having visited, which is exactly
        // what this design refuses to keep.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            requests: 5,
            window: TimeSpan.FromSeconds(30));

        await protection.AuthoriseRequestAsync(Network("ttl"));
        await protection.AuthoriseConversationStartAsync(Network("ttl"), WebChatEndpointId.New());
        await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New());

        IReadOnlyList<string> keys = await redis.KeysAsync();

        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            TimeSpan? remaining = await redis.TimeToLiveAsync(key);

            Assert.NotNull(remaining);
            Assert.True(remaining > TimeSpan.Zero, $"{key} had a non-positive TTL.");
            Assert.True(
                remaining <= TimeSpan.FromHours(24),
                $"{key} had a TTL of {remaining}, longer than any configured window.");
        }
    }

    [Fact]
    public async Task A_counter_disappears_when_its_window_ends()
    {
        // The cleanup guarantee, measured rather than assumed. Nothing sweeps these
        // keys; the TTL is the whole mechanism, so it is worth watching one expire.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            requests: 1,
            window: TimeSpan.FromSeconds(1));

        ClientNetworkKey network = Network("expiring");

        await protection.AuthoriseRequestAsync(network);

        Assert.NotEmpty(await redis.KeysAsync());

        // Refused while the window is open.
        Assert.False((await protection.AuthoriseRequestAsync(network)).Allowed);

        await Task.Delay(TimeSpan.FromMilliseconds(1_400));

        Assert.Empty(await redis.KeysAsync());

        // And allowed again once it has gone, which is what makes the limit a rate
        // rather than a quota.
        Assert.True((await protection.AuthoriseRequestAsync(network)).Allowed);
    }

    [Fact]
    public async Task A_window_is_anchored_and_not_extended_by_later_requests()
    {
        // The documented boundary behaviour. The expiry is set when the counter is
        // created and never touched again -- a sliding expiry would let sustained
        // traffic keep a full counter alive forever and turn a one-minute limit
        // into a permanent lockout.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            requests: 5,
            window: TimeSpan.FromSeconds(30));

        ClientNetworkKey network = Network("anchored");

        await protection.AuthoriseRequestAsync(network);

        string key = Assert.Single(await redis.KeysAsync());
        TimeSpan? first = await redis.TimeToLiveAsync(key);

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));
        await protection.AuthoriseRequestAsync(network);

        TimeSpan? second = await redis.TimeToLiveAsync(key);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(
            second < first,
            $"The window was extended: {first} became {second}.");
    }

    // ---- Key partitioning -------------------------------------------------

    [Fact]
    public async Task Two_networks_have_independent_request_counters()
    {
        // One visitor's flood must not refuse another visitor's first message.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, requests: 1);

        Assert.True((await protection.AuthoriseRequestAsync(Network("one"))).Allowed);
        Assert.False((await protection.AuthoriseRequestAsync(Network("one"))).Allowed);

        Assert.True((await protection.AuthoriseRequestAsync(Network("two"))).Allowed);
    }

    [Fact]
    public async Task One_network_starting_at_two_endpoints_has_two_allowances()
    {
        // Partitioned per endpoint on purpose: one business's visitors must not
        // consume another business's capacity.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, startsPerClient: 1);

        ClientNetworkKey network = Network("two-sites");
        WebChatEndpointId first = WebChatEndpointId.New();
        WebChatEndpointId second = WebChatEndpointId.New();

        Assert.True((await protection.AuthoriseConversationStartAsync(network, first)).Allowed);
        Assert.False((await protection.AuthoriseConversationStartAsync(network, first)).Allowed);

        Assert.True((await protection.AuthoriseConversationStartAsync(network, second)).Allowed);
    }

    [Fact]
    public async Task An_endpoint_has_an_aggregate_limit_no_amount_of_address_rotation_evades()
    {
        // The answer to a botnet. Every per-network limit is bypassed by using more
        // networks, which is cheap; this one does not care where traffic comes
        // from, and it is what bounds how fast conversation rows can be
        // manufactured in one workspace.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            startsPerClient: 50,
            startsPerEndpoint: 3);

        WebChatEndpointId endpoint = WebChatEndpointId.New();

        for (int attacker = 1; attacker <= 3; attacker++)
        {
            Assert.True(
                (await protection.AuthoriseConversationStartAsync(
                    Network($"botnet-{attacker}"),
                    endpoint)).Allowed,
                $"Attempt {attacker} of 3 was refused.");
        }

        // A fresh address every time, and the aggregate still refuses.
        Assert.False(
            (await protection.AuthoriseConversationStartAsync(
                Network("botnet-4"),
                endpoint)).Allowed);
    }

    [Fact]
    public async Task Two_conversations_in_one_workspace_have_independent_session_counters()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, messagesPerConversation: 1);

        ConversationId first = ConversationId.New();
        ConversationId second = ConversationId.New();

        Assert.True((await protection.AuthoriseGenerationAsync(Workspace, first)).Allowed);
        Assert.False((await protection.AuthoriseGenerationAsync(Workspace, first)).Allowed);

        Assert.True((await protection.AuthoriseGenerationAsync(Workspace, second)).Allowed);
    }

    [Fact]
    public async Task Two_workspaces_have_independent_generation_budgets()
    {
        // One business exhausting its budget must never refuse another's customers.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, burst: 1);

        TenantId other = TenantId.New();

        Assert.True(
            (await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New())).Allowed);
        Assert.False(
            (await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New())).Allowed);

        Assert.True(
            (await protection.AuthoriseGenerationAsync(other, ConversationId.New())).Allowed);
    }

    [Fact]
    public async Task The_workspace_budget_bounds_every_conversation_together()
    {
        // The point of an aggregate: spreading traffic across conversations does not
        // buy more generation attempts.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            messagesPerConversation: 10,
            burst: 3);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(
                (await protection.AuthoriseGenerationAsync(
                    Workspace,
                    ConversationId.New())).Allowed,
                $"Attempt {attempt} of 3 was refused.");
        }

        Assert.False(
            (await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New())).Allowed);
    }

    // ---- Atomicity --------------------------------------------------------

    [Fact]
    public async Task A_rejected_decision_consumes_none_of_its_other_counters()
    {
        // The central atomicity property. The workspace burst limit refuses, and
        // the conversation counter -- which had plenty of room -- must be
        // untouched. Consuming it anyway would let a caller already over one limit
        // drain every other limit by retrying.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            messagesPerConversation: 10,
            burst: 1);

        ConversationId conversation = ConversationId.New();

        // Spend the workspace's single burst attempt on a different conversation,
        // so this conversation's own counter has never been touched.
        await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New());

        Assert.False(
            (await protection.AuthoriseGenerationAsync(Workspace, conversation)).Allowed);

        string conversationKey = (await redis.KeysAsync())
            .SingleOrDefault(key => key.Contains(
                conversation.Value.ToString("N"),
                StringComparison.Ordinal))
            ?? string.Empty;

        // Either the key was never created, or it was created and not incremented.
        // Both are "nothing was consumed"; a value of 1 would be the bug.
        long? consumed = conversationKey.Length == 0
            ? null
            : await redis.CounterAsync(conversationKey);

        Assert.True(
            consumed is null or 0,
            $"A rejected decision consumed the conversation counter: {consumed}.");
    }

    [Fact]
    public async Task A_rejected_start_consumes_neither_of_its_counters()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            startsPerClient: 5,
            startsPerEndpoint: 1);

        WebChatEndpointId endpoint = WebChatEndpointId.New();
        ClientNetworkKey network = Network("untouched");

        // Somebody else uses the endpoint's only allowance.
        await protection.AuthoriseConversationStartAsync(Network("first"), endpoint);

        Assert.False(
            (await protection.AuthoriseConversationStartAsync(network, endpoint)).Allowed);

        string clientKey = (await redis.KeysAsync())
            .SingleOrDefault(key =>
                key.Contains("start:client:", StringComparison.Ordinal)
                && key.Contains(network.Value, StringComparison.Ordinal))
            ?? string.Empty;

        long? consumed = clientKey.Length == 0 ? null : await redis.CounterAsync(clientKey);

        Assert.True(
            consumed is null or 0,
            $"A rejected start consumed this network's own allowance: {consumed}.");

        // And the refused caller still has their full allowance at another endpoint.
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(
                (await protection.AuthoriseConversationStartAsync(
                    network,
                    WebChatEndpointId.New())).Allowed,
                $"Attempt {attempt} of 5 was refused.");
        }
    }

    // ---- Concurrency ------------------------------------------------------

    [Fact]
    public async Task Only_the_remaining_attempts_cross_the_boundary_under_concurrency()
    {
        // Redis atomicity, not process timing, decides the winners. Twenty
        // simultaneous new turns against a workspace with five attempts left must
        // produce exactly five permissions -- a read-then-increment would let most
        // of them through.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            messagesPerConversation: 100,
            burst: 5);

        Task<PublicChatDecision>[] attempts =
        [
            .. Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
                protection.AuthoriseGenerationAsync(Workspace, ConversationId.New()))),
        ];

        PublicChatDecision[] decisions = await Task.WhenAll(attempts);

        Assert.Equal(5, decisions.Count(decision => decision.Allowed));
        Assert.Equal(15, decisions.Count(decision => !decision.Allowed));
    }

    [Fact]
    public async Task Concurrent_starts_at_one_endpoint_respect_the_aggregate_exactly()
    {
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(
            client,
            startsPerClient: 100,
            startsPerEndpoint: 4);

        WebChatEndpointId endpoint = WebChatEndpointId.New();

        Task<PublicChatDecision>[] attempts =
        [
            .. Enumerable.Range(0, 25).Select(index => Task.Run(() =>
                protection.AuthoriseConversationStartAsync(
                    Network($"burst-{index}"),
                    endpoint))),
        ];

        PublicChatDecision[] decisions = await Task.WhenAll(attempts);

        Assert.Equal(4, decisions.Count(decision => decision.Allowed));
    }

    // ---- Multiple instances ----------------------------------------------

    [Fact]
    public async Task Two_instances_sharing_one_redis_see_the_same_counters()
    {
        // The whole reason this is not an in-memory limiter. Two independently
        // constructed limiters over two independent connections -- which is exactly
        // the shape of two API instances -- must observe one another's consumption.
        // A per-process limiter would let each of them allow the full limit.
        await using ConnectionMultiplexer first = await redis.ConnectAsync();
        await using ConnectionMultiplexer second = await redis.ConnectAsync();

        Assert.NotSame(first, second);

        PublicChatProtectionOptions limits = Limits(requests: 4);

        RedisPublicChatProtection instanceA = new(first, limits);
        RedisPublicChatProtection instanceB = new(second, limits);

        ClientNetworkKey network = Network("across-instances");

        // Instance A spends the whole allowance.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            Assert.True(
                (await instanceA.AuthoriseRequestAsync(network)).Allowed,
                $"Instance A was refused on attempt {attempt} of 4.");
        }

        // Instance B, which has served nothing, must already see it exhausted.
        PublicChatDecision refused = await instanceB.AuthoriseRequestAsync(network);

        Assert.False(
            refused.Allowed,
            "A second instance allowed a request the first had already exhausted.");
        Assert.True(refused.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Two_instances_share_the_workspace_generation_budget()
    {
        await using ConnectionMultiplexer first = await redis.ConnectAsync();
        await using ConnectionMultiplexer second = await redis.ConnectAsync();

        PublicChatProtectionOptions limits = Limits(burst: 6);

        RedisPublicChatProtection instanceA = new(first, limits);
        RedisPublicChatProtection instanceB = new(second, limits);

        // Alternating instances, so the count can only be right if it is shared.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            RedisPublicChatProtection instance = attempt % 2 == 0 ? instanceA : instanceB;

            Assert.True(
                (await instance.AuthoriseGenerationAsync(
                    Workspace,
                    ConversationId.New())).Allowed,
                $"Attempt {attempt + 1} of 6 was refused.");
        }

        Assert.False(
            (await instanceA.AuthoriseGenerationAsync(Workspace, ConversationId.New())).Allowed);
        Assert.False(
            (await instanceB.AuthoriseGenerationAsync(Workspace, ConversationId.New())).Allowed);
    }

    // ---- Failing closed --------------------------------------------------

    [Fact]
    public async Task An_unreachable_redis_refuses_rather_than_allowing()
    {
        // Fail closed, and by construction: there is no return value meaning
        // "could not tell", so an unreachable store cannot produce an allowed
        // decision however the calling code is written.
        //
        // Port 1 on the loopback interface: nothing listens there, and it is not a
        // port anything is ever configured to use.
        await using ConnectionMultiplexer unreachable =
            await RedisTestServer.ConnectAsync("127.0.0.1:1,connectTimeout=250,syncTimeout=250");

        RedisPublicChatProtection protection = new(unreachable, Limits());

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => protection.AuthoriseRequestAsync(Network("no-store")));

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => protection.AuthoriseConversationStartAsync(
                Network("no-store"),
                WebChatEndpointId.New()));

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => protection.AuthoriseGenerationAsync(Workspace, ConversationId.New()));
    }

    [Fact]
    public async Task The_refusal_tells_an_operator_what_is_wrong_without_telling_a_visitor()
    {
        await using ConnectionMultiplexer unreachable =
            await RedisTestServer.ConnectAsync("127.0.0.1:1,connectTimeout=250,syncTimeout=250");

        PublicChatProtectionUnavailableException failure =
            await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(() =>
                new RedisPublicChatProtection(unreachable, Limits())
                    .AuthoriseRequestAsync(Network("no-store")));

        // The operator's half names the dependency.
        Assert.Contains("protection store", failure.Reason, StringComparison.OrdinalIgnoreCase);

        // The visitor's half names nothing at all.
        foreach (string leak in (string[])["redis", "counter", "lua", "quota", "budget", "127.0.0.1"])
        {
            Assert.DoesNotContain(leak, failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Recovery_needs_no_restart_once_redis_answers_again()
    {
        // The reason AbortOnConnectFail is off: a client that could never reconnect
        // would turn a momentary Redis outage into an API that has to be restarted.
        // Modelled here by a client that starts pointed at nothing and is then
        // pointed at the real server -- the property under test is that a live
        // multiplexer keeps working across a failure, which the reconnecting client
        // provides.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client, requests: 2);
        ClientNetworkKey network = Network("recovery");

        Assert.True((await protection.AuthoriseRequestAsync(network)).Allowed);

        // A round trip that has to re-resolve the connection, exercising the same
        // multiplexer rather than a fresh one.
        Assert.True(client.IsConnected);
        Assert.True((await protection.AuthoriseRequestAsync(network)).Allowed);
        Assert.False((await protection.AuthoriseRequestAsync(network)).Allowed);
    }

    // ---- Privacy ---------------------------------------------------------

    [Fact]
    public async Task No_key_carries_an_address_a_credential_or_a_customers_words()
    {
        // Redis is a protection state store, not a shadow copy of the business.
        // Everything in a key is either a pseudonym or an internal identifier.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client);

        await protection.AuthoriseRequestAsync(Network("privacy"));
        await protection.AuthoriseConversationStartAsync(
            Network("privacy"),
            WebChatEndpointId.New());
        await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New());

        IReadOnlyList<string> keys = await redis.KeysAsync();

        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            foreach (string forbidden in (string[])
            [
                // Addresses, in either family.
                "198.51.100", "203.0.113", "127.0.0.1", "::1", "fe80",
                // Anything shaped like a message, an address book entry or a secret.
                "@", "hello", "message", "text", "email", "token", "secret", "password",
                "instruction",
            ])
            {
                Assert.DoesNotContain(forbidden, key, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Every_key_lives_under_the_configured_prefix()
    {
        // The prefix carries a schema version and separates deployments sharing a
        // server. A key written outside it would be invisible to both.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        RedisPublicChatProtection protection = Build(client);

        await protection.AuthoriseRequestAsync(Network("prefix"));
        await protection.AuthoriseGenerationAsync(Workspace, ConversationId.New());

        foreach (string key in await redis.KeysAsync())
        {
            Assert.StartsWith(redis.KeyPrefix, key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Every_counter_holds_only_a_number()
    {
        // Nothing about the request is kept -- not what was asked, not by whom, not
        // when beyond the TTL Redis maintains itself.
        await using ConnectionMultiplexer client = await redis.ConnectAsync();

        await Build(client).AuthoriseRequestAsync(Network("values"));

        foreach (string key in await redis.KeysAsync())
        {
            long? value = await redis.CounterAsync(key);

            Assert.NotNull(value);
            Assert.True(value > 0);
        }
    }

    // ---- Arrangement -----------------------------------------------------

    /// <summary>
    /// A pseudonymous network key, shaped like a real one but chosen by the test.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from an address here. How a pseudonym is produced
    /// is the key source's business and is tested where it lives; this suite is
    /// about what the counters do with one.
    /// </remarks>
    private static ClientNetworkKey Network(string label) =>
        ClientNetworkKey.From($"k-{label}");

    private RedisPublicChatProtection Build(
        IConnectionMultiplexer client,
        int requests = 100,
        int startsPerClient = 100,
        int startsPerEndpoint = 100,
        int messagesPerConversation = 100,
        int burst = 100,
        TimeSpan? window = null) =>
        new(
            client,
            Limits(
                requests,
                startsPerClient,
                startsPerEndpoint,
                messagesPerConversation,
                burst,
                window));

    /// <summary>
    /// A real options object with the limits one test cares about.
    /// </summary>
    /// <remarks>
    /// The limits a test is not asserting on are set generously rather than
    /// switched off, because there is no way to switch one off — which is the
    /// point. Every decision still evaluates every applicable counter, so these
    /// tests exercise the same multi-counter script production runs.
    /// </remarks>
    private PublicChatProtectionOptions Limits(
        int requests = 100,
        int startsPerClient = 100,
        int startsPerEndpoint = 100,
        int messagesPerConversation = 100,
        int burst = 100,
        TimeSpan? window = null)
    {
        TimeSpan span = window ?? TimeSpan.FromMinutes(2);

        return new PublicChatProtectionOptions
        {
            KeyPrefix = redis.KeyPrefix,
            RequestsPerClient = new RateLimitWindow(requests, span),
            ConversationStartsPerClient = new RateLimitWindow(startsPerClient, span),
            ConversationStartsPerEndpoint = new RateLimitWindow(startsPerEndpoint, span),
            MessagesPerConversation = new RateLimitWindow(messagesPerConversation, span),
            GenerationsPerWorkspaceBurst = new RateLimitWindow(burst, span),
            GenerationsPerWorkspaceSustained =
                new RateLimitWindow(Math.Max(burst, 10_000), span + TimeSpan.FromMinutes(1)),
            GenerationsPerWorkspaceDaily =
                new RateLimitWindow(Math.Max(burst, 100_000), span + TimeSpan.FromMinutes(2)),
        };
    }
}
