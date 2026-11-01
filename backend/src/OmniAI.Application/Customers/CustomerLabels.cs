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
    /// The same rule as <see cref="For"/>, as a code a client words in its
    /// reader's language.
    /// </summary>
    /// <remarks>
    /// The API publishes the code, not the English sentence, wherever a screen
    /// draws the label itself (the conversation list from Phase 37.4). Both come
    /// from this one rule, so they cannot disagree.
    /// </remarks>
    public static CustomerLabelCode CodeFor(
        CustomerDisplayName? displayName,
        IReadOnlyList<ChannelKind> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (displayName is not null)
        {
            return CustomerLabelCode.Named;
        }

        if (channels.Count > 1)
        {
            return CustomerLabelCode.UnnamedCustomer;
        }

        return channels.Count == 1 && channels[0] == ChannelKind.WhatsApp
            ? CustomerLabelCode.WhatsAppCustomer
            : CustomerLabelCode.AnonymousVisitor;
    }

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

        return CodeFor(displayName, channels) switch
        {
            CustomerLabelCode.Named => displayName!.Value,
            CustomerLabelCode.UnnamedCustomer => "Unnamed customer",
            CustomerLabelCode.WhatsAppCustomer => "WhatsApp customer",
            _ => "Anonymous visitor",
        };
    }
}

/// <summary>Which label a customer is shown with.</summary>
/// <remarks>There is no zero member, so an uninitialised value is detectable.</remarks>
public enum CustomerLabelCode
{
    /// <summary>Somebody in the workspace named this customer.</summary>
    Named = 1,

    /// <summary>Unnamed, and reaches the workspace on WhatsApp only.</summary>
    WhatsAppCustomer = 2,

    /// <summary>Unnamed, and reaches the workspace on website chat only (or has no conversation).</summary>
    AnonymousVisitor = 3,

    /// <summary>Unnamed, and reaches the workspace on more than one channel.</summary>
    UnnamedCustomer = 4,
}
