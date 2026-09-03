using OmniAI.Domain.Conversations;

namespace OmniAI.Domain.Tests.Conversations;

public sealed class ChannelKindTests
{
    [Fact]
    public void Supports_exactly_the_four_launch_channels()
    {
        ChannelKind[] defined = Enum.GetValues<ChannelKind>();

        Assert.Equal(4, defined.Length);
        Assert.Contains(ChannelKind.WebChat, defined);
        Assert.Contains(ChannelKind.WhatsApp, defined);
        Assert.Contains(ChannelKind.Instagram, defined);
        Assert.Contains(ChannelKind.Messenger, defined);
    }

    [Fact]
    public void Has_no_uninitialised_default_member()
    {
        // A conversation must always say which channel it is on, so the
        // uninitialised value has to be detectable rather than mean something.
        Assert.False(Enum.IsDefined(default(ChannelKind)));
    }
}
