using System.Security.Cryptography;
using System.Text;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Conversations;

/// <summary>Provider message identities are scoped to a conversation by the turn store.</summary>
internal static class SocialInboundText
{
    public static string Key(ChannelKind channel, string providerId)
    {
        string prefix = channel switch
        {
            ChannelKind.WhatsApp => "wa-",
            ChannelKind.Messenger => "ms-",
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(providerId));
        return prefix + Convert.ToHexStringLower(digest)[..40];
    }
}
