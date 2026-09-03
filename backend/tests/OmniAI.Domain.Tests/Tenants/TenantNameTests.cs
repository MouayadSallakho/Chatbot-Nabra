using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Tenants;

public sealed class TenantNameTests
{
    [Fact]
    public void Create_accepts_a_normal_business_name()
    {
        TenantName name = TenantName.Create("Acme Trading L.L.C.");

        Assert.Equal("Acme Trading L.L.C.", name.Value);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        TenantName name = TenantName.Create("   Acme Trading  \t\n");

        Assert.Equal("Acme Trading", name.Value);
    }

    [Fact]
    public void Create_preserves_interior_spacing_and_casing()
    {
        TenantName name = TenantName.Create("ACME  Trading");

        Assert.Equal("ACME  Trading", name.Value);
    }

    [Fact]
    public void Create_accepts_non_latin_names()
    {
        TenantName name = TenantName.Create("شركة أكمي");

        Assert.Equal("شركة أكمي", name.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Create_rejects_a_missing_or_blank_name(string? value)
    {
        Assert.Throws<DomainValidationException>(() => TenantName.Create(value));
    }

    [Fact]
    public void Create_accepts_a_name_at_the_maximum_length()
    {
        string value = new('a', TenantName.MaxLength);

        TenantName name = TenantName.Create(value);

        Assert.Equal(TenantName.MaxLength, name.Value.Length);
    }

    [Fact]
    public void Create_rejects_a_name_beyond_the_maximum_length()
    {
        string value = new('a', TenantName.MaxLength + 1);

        Assert.Throws<DomainValidationException>(() => TenantName.Create(value));
    }

    [Fact]
    public void Create_measures_length_after_trimming()
    {
        // Padding must not push an otherwise valid name over the limit.
        string value = "  " + new string('a', TenantName.MaxLength) + "  ";

        TenantName name = TenantName.Create(value);

        Assert.Equal(TenantName.MaxLength, name.Value.Length);
    }

    [Fact]
    public void Create_rejects_a_name_containing_a_control_character()
    {
        Assert.Throws<DomainValidationException>(() => TenantName.Create("Acme\u0007Trading"));
    }

    [Fact]
    public void Names_with_the_same_value_are_equal()
    {
        TenantName first = TenantName.Create("Acme");
        TenantName second = TenantName.Create("  Acme  ");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Names_differing_only_in_case_are_not_equal()
    {
        // Case carries meaning in a display name, unlike in a slug.
        Assert.NotEqual(TenantName.Create("Acme"), TenantName.Create("ACME"));
    }

    [Fact]
    public void ToString_returns_the_name()
    {
        Assert.Equal("Acme", TenantName.Create("Acme").ToString());
    }
}
