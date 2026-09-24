using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;

namespace OmniAI.Application.Customers;

/// <summary>
/// The one rule for what a customer is called on screen and in exports.
/// </summary>
/// <remarks>
/// Shared by the directory and by reports, so a customer is never "WhatsApp
/// customer" in one place and something else in a file exported from it.
/// </remarks>
public static class CustomerLabels
{
    /// <summary>
    /// What to call a customer who has no label.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Built from how they reach this workspace, never from what they
    /// said.</strong> Taking a name out of a message, a provider profile or an
    /// email-looking string would be the platform asserting a fact about a real
    /// person that nobody checked, and it would then be shown as though somebody
    /// had.
    /// </para>
    /// <para>
    /// The masked number is deliberately not used here even for a WhatsApp
    /// customer: it belongs on the detail page beside its channel, where it is
    /// clear what it is, rather than as a heading in a list where it reads like a
    /// name. A channel word is the honest label for somebody nobody has named.
    /// </para>
    /// </remarks>
    public static string For(
        CustomerDisplayName? displayName,
        IReadOnlyList<ChannelKind> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (displayName is not null)
        {
            return displayName.Value;
        }

        if (channels.Count > 1)
        {
            return "Unnamed customer";
        }

        return channels.Count == 1 && channels[0] == ChannelKind.WhatsApp
            ? "WhatsApp customer"
            : "Anonymous visitor";
    }
}
