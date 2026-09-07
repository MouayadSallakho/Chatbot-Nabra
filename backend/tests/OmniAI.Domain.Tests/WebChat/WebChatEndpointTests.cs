using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// The website chat endpoint aggregate.
/// </summary>
public sealed class WebChatEndpointTests
{
    private static readonly TenantId Workspace = TenantId.New();
    private static readonly AiAgentId Agent = AiAgentId.New();
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static WebChatEndpoint Create() =>
        WebChatEndpoint.Create(Workspace, Agent, Key(), Noon);

    private static WebChatPublicKey Key() =>
        WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length));

    [Fact]
    public void A_new_endpoint_is_a_draft_and_serves_nobody()
    {
        // Created switched off, so a mistyped agent selection is never live the
        // instant it is saved.
        WebChatEndpoint endpoint = Create();

        Assert.Equal(WebChatEndpointStatus.Draft, endpoint.Status);
        Assert.False(endpoint.IsServingPublicTraffic);
    }

    [Fact]
    public void A_new_endpoint_records_what_it_was_created_from()
    {
        WebChatEndpoint endpoint = Create();

        Assert.Equal(Workspace, endpoint.TenantId);
        Assert.Equal(Agent, endpoint.AiAgentId);
        Assert.Equal(Noon, endpoint.CreatedAt);
        Assert.Equal(Noon, endpoint.UpdatedAt);
    }

    [Fact]
    public void Activating_puts_it_into_service()
    {
        WebChatEndpoint endpoint = Create();

        endpoint.Activate(Noon.AddHours(1));

        Assert.Equal(WebChatEndpointStatus.Active, endpoint.Status);
        Assert.True(endpoint.IsServingPublicTraffic);
        Assert.Equal(Noon.AddHours(1), endpoint.UpdatedAt);
    }

    [Fact]
    public void Disabling_takes_it_out_of_service()
    {
        WebChatEndpoint endpoint = Create();
        endpoint.Activate(Noon.AddHours(1));

        endpoint.Disable(Noon.AddHours(2));

        Assert.Equal(WebChatEndpointStatus.Disabled, endpoint.Status);
        Assert.False(endpoint.IsServingPublicTraffic);
    }

    [Fact]
    public void Activating_an_active_endpoint_changes_nothing_at_all()
    {
        // Idempotent, and it does not move the version either -- otherwise a
        // no-op could churn a value other requests hold preconditions against.
        WebChatEndpoint endpoint = Create();
        endpoint.Activate(Noon.AddHours(1));

        endpoint.Activate(Noon.AddHours(5));

        Assert.Equal(WebChatEndpointStatus.Active, endpoint.Status);
        Assert.Equal(Noon.AddHours(1), endpoint.UpdatedAt);
    }

    [Fact]
    public void The_change_instant_never_moves_backwards()
    {
        WebChatEndpoint endpoint = Create();
        endpoint.Activate(Noon.AddHours(3));

        endpoint.Disable(Noon.AddHours(1));

        Assert.Equal(WebChatEndpointStatus.Disabled, endpoint.Status);
        Assert.Equal(Noon.AddHours(3), endpoint.UpdatedAt);
    }

    [Fact]
    public void An_endpoint_must_belong_to_a_workspace_and_name_an_agent()
    {
        Assert.Throws<DomainValidationException>(
            () => WebChatEndpoint.Create(default, Agent, Key(), Noon));

        Assert.Throws<DomainValidationException>(
            () => WebChatEndpoint.Create(Workspace, default, Key(), Noon));

        Assert.Throws<DomainValidationException>(
            () => WebChatEndpoint.Create(Workspace, Agent, default, Noon));
    }

    [Fact]
    public void A_stored_endpoint_is_rebuilt_exactly_as_it_was_written()
    {
        WebChatEndpointId id = WebChatEndpointId.New();

        WebChatEndpoint restored = WebChatEndpoint.FromStored(
            id,
            Workspace,
            Agent,
            Key(),
            WebChatEndpointStatus.Disabled,
            Noon,
            Noon.AddDays(1));

        Assert.Equal(id, restored.Id);
        Assert.Equal(WebChatEndpointStatus.Disabled, restored.Status);
        Assert.Equal(Noon.AddDays(1), restored.UpdatedAt);
    }
}

/// <summary>
/// The opaque key in a hosted chat page's address.
/// </summary>
public sealed class WebChatPublicKeyTests
{
    [Fact]
    public void A_key_is_exactly_the_published_length()
    {
        Assert.Throws<DomainValidationException>(
            () => WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length - 1)));

        Assert.Throws<DomainValidationException>(
            () => WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_key_is_refused(string? value) =>
        Assert.Throws<DomainValidationException>(() => WebChatPublicKey.Create(value));

    [Fact]
    public void A_key_holds_only_base64url_characters()
    {
        // Anything else means somebody has edited a URL, and it must not reach a
        // database query even as a parameter.
        Assert.Throws<DomainValidationException>(
            () => WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length - 1) + "%"));

        Assert.Throws<DomainValidationException>(
            () => WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length - 1) + "'"));
    }

    [Fact]
    public void Hyphen_and_underscore_are_accepted_because_base64url_uses_them()
    {
        WebChatPublicKey key = WebChatPublicKey.Create(
            "-_" + new string('Z', WebChatPublicKey.Length - 2));

        Assert.StartsWith("-_", key.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_key_from_a_url_is_answered_without_an_exception()
    {
        // The public route is reached by anybody with a browser, and a typo is not
        // an exceptional condition worth a stack trace.
        Assert.False(WebChatPublicKey.TryCreate("not-a-key", out WebChatPublicKey parsed));
        Assert.True(parsed.IsEmpty);

        Assert.True(WebChatPublicKey.TryCreate(
            new string('q', WebChatPublicKey.Length),
            out WebChatPublicKey good));
        Assert.False(good.IsEmpty);
    }
}
