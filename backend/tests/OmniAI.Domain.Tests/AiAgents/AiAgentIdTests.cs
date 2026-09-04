using System.Globalization;
using OmniAI.Domain.AiAgents;

namespace OmniAI.Domain.Tests.AiAgents;

public sealed class AiAgentIdTests
{
    [Fact]
    public void From_keeps_the_supplied_identifier()
    {
        Guid value = Guid.CreateVersion7();

        AiAgentId id = AiAgentId.From(value);

        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void From_rejects_an_empty_guid()
    {
        Assert.Throws<DomainValidationException>(() => AiAgentId.From(Guid.Empty));
    }

    [Fact]
    public void New_produces_a_usable_identifier()
    {
        AiAgentId id = AiAgentId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.False(id.IsEmpty);
    }

    [Fact]
    public void New_produces_a_distinct_identifier_each_time()
    {
        Assert.NotEqual(AiAgentId.New(), AiAgentId.New());
    }

    [Fact]
    public void New_produces_a_version_7_uuid()
    {
        // The ordering property the design depends on comes from the version
        // nibble, so it is worth asserting rather than assuming.
        AiAgentId id = AiAgentId.New();

        Assert.Equal(7, id.Value.ToByteArray(bigEndian: true)[6] >> 4);
    }

    [Fact]
    public void Ids_wrapping_the_same_guid_are_equal()
    {
        Guid value = Guid.CreateVersion7();

        AiAgentId first = AiAgentId.From(value);
        AiAgentId second = AiAgentId.From(value);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Ids_wrapping_different_guids_are_not_equal()
    {
        AiAgentId first = AiAgentId.From(Guid.CreateVersion7());
        AiAgentId second = AiAgentId.From(Guid.CreateVersion7());

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void The_default_value_is_empty_and_reports_itself_as_such()
    {
        // C# permits default(AiAgentId) for any struct and no factory can produce
        // it, so the type has to make the hole visible rather than pretend it
        // cannot happen. Matches the accepted TenantId and ConversationId
        // behaviour.
        AiAgentId uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
        Assert.Equal(Guid.Empty, uninitialised.Value);
    }

    [Fact]
    public void A_created_id_is_never_empty()
    {
        Assert.False(AiAgentId.New().IsEmpty);
        Assert.False(AiAgentId.From(Guid.CreateVersion7()).IsEmpty);
    }

    [Fact]
    public void ToString_is_the_invariant_dashed_form()
    {
        Guid value = Guid.CreateVersion7();

        AiAgentId id = AiAgentId.From(value);

        Assert.Equal(value.ToString("D", CultureInfo.InvariantCulture), id.ToString());
    }
}
