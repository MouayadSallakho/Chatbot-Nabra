using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Tenants;

public sealed class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-syria")]
    [InlineData("techno-solutions")]
    [InlineData("a1b")]
    [InlineData("123")]
    [InlineData("a-b-c-d")]
    public void Create_accepts_a_well_formed_slug(string value)
    {
        TenantSlug slug = TenantSlug.Create(value);

        Assert.Equal(value, slug.Value);
    }

    [Fact]
    public void Create_lowercases_the_slug()
    {
        TenantSlug slug = TenantSlug.Create("ACME-Syria");

        Assert.Equal("acme-syria", slug.Value);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace()
    {
        TenantSlug slug = TenantSlug.Create("  acme-syria \n");

        Assert.Equal("acme-syria", slug.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_or_blank_slug(string? value)
    {
        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Theory]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("-acme-")]
    [InlineData("---")]
    public void Create_rejects_a_leading_or_trailing_hyphen(string value)
    {
        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Theory]
    [InlineData("ac--me")]
    [InlineData("a--b")]
    [InlineData("acme--syria")]
    [InlineData("a-b--c")]
    public void Create_rejects_consecutive_hyphens(string value)
    {
        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Theory]
    [InlineData("acme syria")]      // interior space
    [InlineData("acme_syria")]      // underscore
    [InlineData("acme.syria")]      // dot
    [InlineData("acme/syria")]      // slash
    [InlineData("acmé")]            // accented letter
    [InlineData("شركة")]            // non-ascii script
    [InlineData("acme!")]           // punctuation
    [InlineData("acme syria co")]   // multiple words
    public void Create_rejects_characters_outside_the_slug_grammar(string value)
    {
        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Fact]
    public void Create_rejects_a_slug_shorter_than_the_minimum()
    {
        string value = new('a', TenantSlug.MinLength - 1);

        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Fact]
    public void Create_accepts_a_slug_at_the_minimum_length()
    {
        string value = new('a', TenantSlug.MinLength);

        Assert.Equal(value, TenantSlug.Create(value).Value);
    }

    [Fact]
    public void Create_accepts_a_slug_at_the_maximum_length()
    {
        string value = new('a', TenantSlug.MaxLength);

        Assert.Equal(value, TenantSlug.Create(value).Value);
    }

    [Fact]
    public void Create_rejects_a_slug_beyond_the_maximum_length()
    {
        string value = new('a', TenantSlug.MaxLength + 1);

        Assert.Throws<DomainValidationException>(() => TenantSlug.Create(value));
    }

    [Fact]
    public void Create_measures_length_after_trimming()
    {
        string value = "  " + new string('a', TenantSlug.MaxLength) + "  ";

        Assert.Equal(TenantSlug.MaxLength, TenantSlug.Create(value).Value.Length);
    }

    [Fact]
    public void Create_does_not_repair_a_malformed_slug()
    {
        // A space is rejected outright rather than being turned into a hyphen,
        // so a mistyped slug cannot silently become a different tenant's slug.
        Assert.Throws<DomainValidationException>(() => TenantSlug.Create("acme syria"));
    }

    [Fact]
    public void Slugs_differing_only_in_case_or_padding_are_equal()
    {
        TenantSlug first = TenantSlug.Create("acme");
        TenantSlug second = TenantSlug.Create("  ACME ");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Different_slugs_are_not_equal()
    {
        Assert.NotEqual(TenantSlug.Create("acme"), TenantSlug.Create("acme-syria"));
    }

    [Fact]
    public void ToString_returns_the_canonical_slug()
    {
        Assert.Equal("acme-syria", TenantSlug.Create("ACME-Syria").ToString());
    }
}
