using OmniAI.Application.Concurrency;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The encoding that turns PostgreSQL's row version into the opaque token
/// clients see as an <c>ETag</c>.
/// </summary>
/// <remarks>
/// No database is needed -- the behaviour is a pure transformation -- but it
/// belongs in this project because the value it encodes is a PostgreSQL system
/// column, and this class is the boundary that stops that fact travelling any
/// further.
/// </remarks>
public sealed class RowVersionTokensTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(12345u)]
    [InlineData(uint.MaxValue)]
    [InlineData(2147483648u)]
    public void A_row_version_round_trips_exactly(uint rowVersion)
    {
        // Exact round-tripping is the whole contract: a token that decoded to a
        // different number would either refuse valid writes or, far worse, permit
        // one against a version the caller never saw.
        ConcurrencyToken token = RowVersionTokens.Encode(rowVersion);

        Assert.True(RowVersionTokens.TryDecode(token, out uint decoded));
        Assert.Equal(rowVersion, decoded);
    }

    [Fact]
    public void The_same_row_version_always_produces_the_same_token()
    {
        // Stable across calls, so a token issued by one request matches one
        // computed by another. Anything random or time-based here would break
        // every conditional write.
        Assert.Equal(RowVersionTokens.Encode(12345u), RowVersionTokens.Encode(12345u));
    }

    [Fact]
    public void Different_row_versions_produce_different_tokens()
    {
        Assert.NotEqual(RowVersionTokens.Encode(1u), RowVersionTokens.Encode(2u));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(12345u)]
    [InlineData(uint.MaxValue)]
    public void A_token_does_not_contain_the_row_version_in_readable_form(uint rowVersion)
    {
        // Opaque, so a client cannot build logic on the internals and then break
        // when the encoding changes -- and so the header does not advertise the
        // storage engine.
        string token = RowVersionTokens.Encode(rowVersion).Value;

        Assert.DoesNotContain(
            rowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            token,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_is_safe_to_put_in_a_header_unescaped()
    {
        // It travels inside a quoted ETag, so it must never contain a quote, a
        // backslash, a comma or whitespace -- any of which would break the header
        // or turn one tag into what a parser reads as several. Asserting the
        // base64url alphabet is the strongest form of that claim: it excludes
        // every one of those characters and everything else that would need
        // escaping, including the '+' and '/' of ordinary base64.
        for (uint candidate = 0; candidate < 4096; candidate++)
        {
            string token = RowVersionTokens.Encode(candidate).Value;

            Assert.NotEmpty(token);
            Assert.All(
                token,
                character => Assert.True(
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '_',
                    $"'{character}' is outside the base64url alphabet."));
        }
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("!!!!")]
    [InlineData("AAAA-AAAA")]
    [InlineData("A")]
    [InlineData("AAAAAAAAAAAA")]
    public void A_value_this_platform_never_issued_does_not_decode(string value)
    {
        // Too short, too long, or not base64url at all. Refusing to decode is what
        // turns it into a failed precondition rather than an accidental match.
        Assert.False(RowVersionTokens.TryDecode(ConcurrencyToken.From(value), out _));
    }

    [Fact]
    public void An_empty_token_does_not_decode()
    {
        Assert.False(RowVersionTokens.TryDecode(default, out _));
    }

    [Fact]
    public void A_failed_decode_reports_a_row_version_of_zero_rather_than_a_stale_value()
    {
        RowVersionTokens.TryDecode(RowVersionTokens.Encode(9999u), out uint first);
        Assert.Equal(9999u, first);

        Assert.False(RowVersionTokens.TryDecode(ConcurrencyToken.From("rubbish"), out uint second));
        Assert.Equal(0u, second);
    }
}
