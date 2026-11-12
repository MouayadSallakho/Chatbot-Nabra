using OmniAI.Application.Customers;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;

namespace OmniAI.Application.Tests.Customers;

/// <summary>
/// The one label rule the conversation reads and, since Phase 38.7a, the
/// customer directory both speak.
/// </summary>
public sealed class CustomerLabelsTests
{
    public static TheoryData<string?, ChannelKind[], CustomerLabelCode, string> Cases => new()
    {
        { "Layla", [ChannelKind.WebChat], CustomerLabelCode.Named, "Layla" },
        { "Layla", [ChannelKind.WebChat, ChannelKind.WhatsApp], CustomerLabelCode.Named, "Layla" },
        { null, [ChannelKind.WhatsApp], CustomerLabelCode.WhatsAppCustomer, "WhatsApp customer" },
        { null, [ChannelKind.WebChat], CustomerLabelCode.AnonymousVisitor, "Anonymous visitor" },
        { null, [], CustomerLabelCode.AnonymousVisitor, "Anonymous visitor" },
        { null, [ChannelKind.WebChat, ChannelKind.WhatsApp], CustomerLabelCode.UnnamedCustomer, "Unnamed customer" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_customer_gets_one_code_and_the_english_label_follows_it(
        string? name,
        ChannelKind[] channels,
        CustomerLabelCode code,
        string label)
    {
        CustomerDisplayName? displayName = name is null ? null : CustomerDisplayName.Create(name);

        Assert.Equal(code, CustomerLabels.CodeFor(displayName, channels));
        Assert.Equal(label, CustomerLabels.For(displayName, channels));
    }

    [Fact]
    public void A_short_code_is_six_crockford_characters_and_the_same_every_time()
    {
        CustomerId customer = CustomerId.New();

        string code = CustomerShortCode.For(customer);

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{6}$", code);
        Assert.Equal(code, CustomerShortCode.For(customer.Value));
    }
}
