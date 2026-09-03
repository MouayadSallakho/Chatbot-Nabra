using System.Globalization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Tenants;

public sealed class TenantIdTests
{
    [Fact]
    public void From_keeps_the_supplied_identifier()
    {
        Guid value = Guid.CreateVersion7();

        TenantId id = TenantId.From(value);

        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void From_rejects_an_empty_guid()
    {
        Assert.Throws<DomainValidationException>(() => TenantId.From(Guid.Empty));
    }

    [Fact]
    public void New_produces_a_usable_identifier()
    {
        TenantId id = TenantId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.False(id.IsEmpty);
    }

    [Fact]
    public void New_produces_a_distinct_identifier_each_time()
    {
        TenantId first = TenantId.New();
        TenantId second = TenantId.New();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Ids_wrapping_the_same_guid_are_equal()
    {
        Guid value = Guid.CreateVersion7();

        TenantId first = TenantId.From(value);
        TenantId second = TenantId.From(value);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Ids_wrapping_different_guids_are_not_equal()
    {
        TenantId first = TenantId.From(Guid.CreateVersion7());
        TenantId second = TenantId.From(Guid.CreateVersion7());

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void Default_instance_reports_itself_as_empty()
    {
        // C# always permits default(T) for a struct, so the uninitialised value
        // has to be detectable even though no factory can produce it.
        TenantId uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }

    [Fact]
    public void ToString_returns_the_plain_guid_form()
    {
        Guid value = Guid.CreateVersion7();

        TenantId id = TenantId.From(value);

        Assert.Equal(value.ToString("D", CultureInfo.InvariantCulture), id.ToString());
    }
}
