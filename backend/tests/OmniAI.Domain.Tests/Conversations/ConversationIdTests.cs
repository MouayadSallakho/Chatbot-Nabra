using System.Globalization;
using OmniAI.Domain.Conversations;

namespace OmniAI.Domain.Tests.Conversations;

public sealed class ConversationIdTests
{
    [Fact]
    public void From_keeps_the_supplied_identifier()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(value, ConversationId.From(value).Value);
    }

    [Fact]
    public void From_rejects_an_empty_guid()
    {
        Assert.Throws<DomainValidationException>(() => ConversationId.From(Guid.Empty));
    }

    [Fact]
    public void New_produces_a_usable_identifier()
    {
        ConversationId id = ConversationId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.False(id.IsEmpty);
    }

    [Fact]
    public void New_produces_a_distinct_identifier_each_time()
    {
        Assert.NotEqual(ConversationId.New(), ConversationId.New());
    }

    [Fact]
    public void Ids_wrapping_the_same_guid_are_equal()
    {
        Guid value = Guid.CreateVersion7();

        ConversationId first = ConversationId.From(value);
        ConversationId second = ConversationId.From(value);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Ids_wrapping_different_guids_are_not_equal()
    {
        ConversationId first = ConversationId.New();
        ConversationId second = ConversationId.New();

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void Default_instance_reports_itself_as_empty()
    {
        ConversationId uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }

    [Fact]
    public void ToString_returns_the_plain_guid_form()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(
            value.ToString("D", CultureInfo.InvariantCulture),
            ConversationId.From(value).ToString());
    }
}
