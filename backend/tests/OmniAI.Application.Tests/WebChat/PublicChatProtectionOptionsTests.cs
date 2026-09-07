using OmniAI.Application.WebChat.Protection;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// The policy that decides how much a stranger may consume, checked at startup.
/// </summary>
/// <remarks>
/// <strong>These numbers are the difference between a chat page and a billing
/// incident, so a mistyped one must be a deployment failure rather than a rate
/// limit somebody's customer discovers.</strong> Every assertion here is about
/// refusing a policy that does not mean what it appears to mean.
/// </remarks>
public sealed class PublicChatProtectionOptionsTests
{
    [Fact]
    public void The_shipped_defaults_are_a_valid_policy()
    {
        // The one that would be embarrassing to get wrong: a deployment that
        // configures nothing must start.
        new PublicChatProtectionOptions().Validate();
    }

    [Fact]
    public void The_shipped_defaults_are_the_documented_ones()
    {
        // Pinned deliberately. These exact numbers are written into the
        // architecture document and the phase report, and a silent edit here would
        // make that documentation wrong without anything failing.
        PublicChatProtectionOptions limits = new();

        Assert.Equal(new RateLimitWindow(5, TimeSpan.FromMinutes(5)),
            limits.ConversationStartsPerClient);
        Assert.Equal(new RateLimitWindow(60, TimeSpan.FromMinutes(5)),
            limits.ConversationStartsPerEndpoint);
        Assert.Equal(new RateLimitWindow(30, TimeSpan.FromMinutes(1)),
            limits.RequestsPerClient);
        Assert.Equal(new RateLimitWindow(10, TimeSpan.FromMinutes(1)),
            limits.MessagesPerConversation);
        Assert.Equal(new RateLimitWindow(30, TimeSpan.FromMinutes(1)),
            limits.GenerationsPerWorkspaceBurst);
        Assert.Equal(new RateLimitWindow(600, TimeSpan.FromHours(1)),
            limits.GenerationsPerWorkspaceSustained);
        Assert.Equal(new RateLimitWindow(5_000, TimeSpan.FromHours(24)),
            limits.GenerationsPerWorkspaceDaily);
        Assert.Equal(TimeSpan.FromHours(24), limits.SessionLifetime);
    }

    [Fact]
    public void A_limit_of_zero_is_refused()
    {
        // Zero would switch public chat off, which is what disabling the endpoint
        // is for. Refused rather than obeyed, because a deployment that reached it
        // by a typo would be silently dark.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                RequestsPerClient = new RateLimitWindow(0, TimeSpan.FromMinutes(1)),
            }.Validate());

        Assert.Contains("RequestsPerClient", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_limit_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                MessagesPerConversation = new RateLimitWindow(-1, TimeSpan.FromMinutes(1)),
            }.Validate());
    }

    [Fact]
    public void A_window_of_nothing_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                RequestsPerClient = new RateLimitWindow(10, TimeSpan.Zero),
            }.Validate());
    }

    [Fact]
    public void A_window_longer_than_a_day_is_refused()
    {
        // A counter that lives for a week is not a rate limit, it is a quota with
        // no reset anybody would notice -- and its Redis key would outlive any
        // reasonable expectation of one.
        Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                GenerationsPerWorkspaceDaily =
                    new RateLimitWindow(5_000, TimeSpan.FromDays(7)),
            }.Validate());
    }

    [Fact]
    public void A_wider_workspace_window_must_actually_be_wider()
    {
        // Two settings that disagree about which is the outer bound. One of them
        // would be doing nothing, and nobody would be able to tell which.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                GenerationsPerWorkspaceBurst =
                    new RateLimitWindow(30, TimeSpan.FromHours(2)),
            }.Validate());

        Assert.Contains(
            "GenerationsPerWorkspaceSustained",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_daily_ceiling_below_the_hourly_allowance_is_refused()
    {
        // Not a stricter policy: the hourly limit could never be reached, so it is
        // not the limit it appears to be.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                GenerationsPerWorkspaceDaily =
                    new RateLimitWindow(100, TimeSpan.FromHours(24)),
            }.Validate());

        Assert.Contains("fewer attempts", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_lifetime_of_nothing_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions { SessionLifetime = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void A_session_lifetime_longer_than_a_month_is_refused()
    {
        // An anonymous credential that outlives a month is not a session. It is a
        // permanent bearer token for a stranger's conversation.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions
            {
                SessionLifetime = TimeSpan.FromDays(31),
            }.Validate());

        Assert.Contains("SessionLifetime", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_key_prefix_is_refused()
    {
        // The prefix carries the schema version and separates environments sharing
        // one server. Blank would silently merge them.
        Assert.Throws<InvalidOperationException>(() =>
            new PublicChatProtectionOptions { KeyPrefix = "  " }.Validate());
    }

    [Fact]
    public void Every_message_names_the_setting_an_operator_has_to_change()
    {
        // A validation failure at startup is read by somebody who has a deployment
        // that will not boot. "Invalid configuration" would be useless to them.
        foreach (PublicChatProtectionOptions broken in Broken())
        {
            InvalidOperationException failure =
                Assert.Throws<InvalidOperationException>(broken.Validate);

            Assert.Contains(
                PublicChatProtectionOptions.SectionName,
                failure.Message,
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<PublicChatProtectionOptions> Broken()
    {
        yield return new PublicChatProtectionOptions
        {
            RequestsPerClient = new RateLimitWindow(0, TimeSpan.FromMinutes(1)),
        };

        yield return new PublicChatProtectionOptions
        {
            ConversationStartsPerClient = new RateLimitWindow(5, TimeSpan.Zero),
        };

        yield return new PublicChatProtectionOptions { SessionLifetime = TimeSpan.Zero };

        yield return new PublicChatProtectionOptions { KeyPrefix = string.Empty };
    }
}
