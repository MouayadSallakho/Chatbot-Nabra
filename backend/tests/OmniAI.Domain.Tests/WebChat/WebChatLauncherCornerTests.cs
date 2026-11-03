using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>Phase 37.5a, row 22: corners that follow the host page's direction.</summary>
public sealed class WebChatLauncherCornerTests
{
    [Theory]
    [InlineData(WebChatLauncherPosition.Start)]
    [InlineData(WebChatLauncherPosition.End)]
    [InlineData(WebChatLauncherPosition.Left)]
    [InlineData(WebChatLauncherPosition.Right)]
    public void Every_offered_corner_is_a_valid_setting(WebChatLauncherPosition position)
    {
        WebChatWidgetSettings settings = WebChatWidgetSettings.Create(
            greeting: null,
            WebChatAccentColor.Default,
            position,
            WebChatTheme.System);

        Assert.Equal(position, settings.Position);
    }

    [Fact]
    public void The_physical_corners_keep_their_stored_values()
    {
        // Stored as the name, but a reordered enum is still a trap: the old values
        // must not have moved.
        Assert.Equal(0, (int)WebChatLauncherPosition.Right);
        Assert.Equal(1, (int)WebChatLauncherPosition.Left);
        Assert.Equal(WebChatLauncherPosition.Right, WebChatWidgetSettings.Default.Position);
    }

    [Fact]
    public void A_corner_outside_the_four_carries_its_code()
    {
        DomainValidationException refused = Assert.Throws<DomainValidationException>(
            () => WebChatWidgetSettings.Create(null, WebChatAccentColor.Default, (WebChatLauncherPosition)7, WebChatTheme.System));

        Assert.Equal("website-chat.launcher_position.invalid", refused.Code);
    }
}
