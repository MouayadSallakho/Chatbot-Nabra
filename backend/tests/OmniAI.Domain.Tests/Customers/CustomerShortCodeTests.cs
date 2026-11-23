using OmniAI.Domain.Customers;

namespace OmniAI.Domain.Tests.Customers;

/// <summary>
/// The short code that tells unnamed customers apart (Phase 37.4).
/// </summary>
public sealed class CustomerShortCodeTests
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly DateTimeOffset Instant =
        new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_code_is_six_crockford_characters()
    {
        string code = CustomerShortCode.For(CustomerId.New());

        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.Contains(character, Crockford));
    }

    [Fact]
    public void A_code_search_includes_every_colliding_id_suffix()
    {
        string[] suffixes = ["0abcdef1", "4abcdef1", "8abcdef1", "cabcdef1"];
        foreach (string suffix in suffixes)
        {
            Guid id = Guid.Parse("00000000-0000-7000-8000-0000" + suffix);
            Assert.Equal(suffixes, CustomerShortCode.MatchingIdSuffixes(CustomerShortCode.For(id).ToLowerInvariant()));
        }

        Assert.Empty(CustomerShortCode.MatchingIdSuffixes("HELLO!"));
    }

    [Fact]
    public void The_same_customer_always_has_the_same_code()
    {
        CustomerId customer = CustomerId.New();

        Assert.Equal(CustomerShortCode.For(customer), CustomerShortCode.For(customer));
        Assert.Equal(CustomerShortCode.For(customer), CustomerShortCode.For(customer.Value));
    }

    [Fact]
    public void Customers_created_a_millisecond_apart_get_different_codes()
    {
        // The leading characters of a UUIDv7 are its timestamp, so a code cut from
        // there would repeat for everybody who arrived in the same minute.
        Guid first = Guid.CreateVersion7(Instant);
        Guid second = Guid.CreateVersion7(Instant.AddMilliseconds(1));

        Assert.NotEqual(CustomerShortCode.For(first), CustomerShortCode.For(second));
    }

    [Fact]
    public void Many_customers_created_in_the_same_millisecond_are_told_apart()
    {
        // Thirty random bits: a thousand ids from one instant collide with a
        // probability under one in two thousand, so a collision here means the
        // code is not reading the random part.
        HashSet<string> codes = [];

        for (int index = 0; index < 1000; index++)
        {
            codes.Add(CustomerShortCode.For(Guid.CreateVersion7(Instant)));
        }

        Assert.True(codes.Count >= 999, $"Only {codes.Count} distinct codes for 1000 ids.");
    }

    [Fact]
    public void The_code_comes_from_the_end_of_the_id_and_ignores_its_timestamp()
    {
        // Same random tail, timestamps a year apart: the same code.
        Guid early = Guid.Parse("01900000-0000-7000-8000-00000a1b2c3d");
        Guid late = Guid.Parse("019a0000-0000-7000-8000-00000a1b2c3d");

        Assert.Equal(CustomerShortCode.For(early), CustomerShortCode.For(late));

        // Same timestamp, a different tail: a different code.
        Guid other = Guid.Parse("01900000-0000-7000-8000-00000a1b2c3e");

        Assert.NotEqual(CustomerShortCode.For(early), CustomerShortCode.For(other));
    }

    [Fact]
    public void A_known_id_has_a_known_code()
    {
        // The last 30 bits of ...ffffffff are all ones: "ZZZZZZ". Of ...00000000,
        // all zeros: "000000". Pins the derivation so it cannot drift silently.
        Assert.Equal("ZZZZZZ", CustomerShortCode.For(Guid.Parse("01900000-0000-7000-8000-0000ffffffff")));
        Assert.Equal("000000", CustomerShortCode.For(Guid.Parse("01900000-0000-7000-8000-ffff00000000")));
    }

    [Fact]
    public void An_empty_id_has_no_code()
    {
        Assert.Throws<DomainValidationException>(() => CustomerShortCode.For(Guid.Empty));
    }
}
