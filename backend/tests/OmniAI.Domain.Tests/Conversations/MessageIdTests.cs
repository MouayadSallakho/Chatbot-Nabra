using System.Globalization;
using OmniAI.Domain.Conversations;

namespace OmniAI.Domain.Tests.Conversations;

public sealed class MessageIdTests
{
    [Fact]
    public void From_keeps_the_supplied_identifier()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(value, MessageId.From(value).Value);
    }

    [Fact]
    public void From_rejects_an_empty_guid()
    {
        Assert.Throws<DomainValidationException>(() => MessageId.From(Guid.Empty));
    }

    [Fact]
    public void New_produces_a_usable_identifier()
    {
        MessageId id = MessageId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.False(id.IsEmpty);
    }

    [Fact]
    public void New_produces_a_distinct_identifier_each_time()
    {
        Assert.NotEqual(MessageId.New(), MessageId.New());
    }

    [Fact]
    public void Ids_wrapping_the_same_guid_are_equal()
    {
        Guid value = Guid.CreateVersion7();

        MessageId first = MessageId.From(value);
        MessageId second = MessageId.From(value);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Ids_wrapping_different_guids_are_not_equal()
    {
        Assert.NotEqual(MessageId.New(), MessageId.New());
    }

    [Fact]
    public void Default_instance_reports_itself_as_empty()
    {
        MessageId uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }

    [Fact]
    public void ToString_returns_the_plain_guid_form()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(
            value.ToString("D", CultureInfo.InvariantCulture),
            MessageId.From(value).ToString());
    }
}
