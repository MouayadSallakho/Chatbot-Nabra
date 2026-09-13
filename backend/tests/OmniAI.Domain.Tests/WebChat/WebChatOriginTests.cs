using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// The website an embedded widget is allowed to run on.
/// </summary>
/// <remarks>
/// <para>
/// Every test here is about one property: two spellings of one website must
/// produce one value, and anything that is not a website must produce no value at
/// all. That is what lets the runtime's check be equality — and equality is the
/// only comparison that cannot be tricked by a cleverly shaped hostname.
/// </para>
/// <para>
/// <strong>This is browser embedding policy, not abuse protection.</strong> An
/// approved website still goes through every rate limit, budget and lock the
/// public chat has; this type only decides where the widget may appear.
/// </para>
/// </remarks>
public sealed class WebChatOriginTests
{
    private static string Canonical(string candidate) => WebChatOrigin.Create(candidate).Value;

    private static string Refusal(string? candidate)
    {
        Assert.False(WebChatOrigin.TryCreate(candidate, out _, out string problem));

        return problem;
    }

    [Theory]
    [InlineData("https://www.example.com", "https://www.example.com")]
    [InlineData("https://www.example.com/", "https://www.example.com")]
    [InlineData("HTTPS://WWW.EXAMPLE.COM", "https://www.example.com")]
    [InlineData("  https://www.example.com  ", "https://www.example.com")]
    [InlineData("https://www.example.com:443", "https://www.example.com")]
    [InlineData("https://www.example.com:8443", "https://www.example.com:8443")]
    public void One_website_written_several_ways_becomes_one_value(string candidate, string expected)
    {
        // Storing these as separate entries would let somebody "remove" a website
        // that was never separately approved, and leave the one that was.
        Assert.Equal(expected, Canonical(candidate));
    }

    [Fact]
    public void Two_spellings_of_one_website_are_equal()
    {
        Assert.Equal(
            WebChatOrigin.Create("https://Example.com:443/"),
            WebChatOrigin.Create("https://example.com"));
    }

    [Fact]
    public void An_international_domain_is_stored_as_the_browser_reports_it()
    {
        // event.origin carries the punycode host. Storing the unicode spelling
        // would produce an entry nothing could ever match.
        Assert.Equal("https://xn--bcher-kva.example", Canonical("https://bücher.example"));
        Assert.Equal(
            WebChatOrigin.Create("https://bücher.example"),
            WebChatOrigin.Create("https://xn--bcher-kva.example"));
    }

    [Fact]
    public void An_ipv6_host_keeps_its_brackets()
    {
        Assert.Equal("https://[2001:db8::1]", Canonical("https://[2001:db8::1]"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://*")]
    public void A_wildcard_is_refused_rather_than_interpreted(string candidate)
    {
        // There is no safe reading of a wildcard here. "*.example.com" would have
        // to mean trusting whoever can obtain any subdomain, which on shared
        // hosting is anybody at all.
        Assert.NotEmpty(Refusal(candidate));
    }

    [Theory]
    [InlineData("https://www.example.com/support")]
    [InlineData("https://www.example.com/?utm=1")]
    [InlineData("https://www.example.com/#chat")]
    [InlineData("https://user:secret@example.com")]
    public void A_page_address_is_not_a_website(string candidate)
    {
        // A path, a query, a fragment or credentials all mean somebody pasted the
        // address of a page. Trimming silently would approve more than they read.
        Assert.NotEmpty(Refusal(candidate));
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("about:blank")]
    public void Only_http_schemes_can_name_a_website(string candidate)
    {
        Assert.NotEmpty(Refusal(candidate));
    }

    [Fact]
    public void The_opaque_origin_is_refused_because_it_identifies_nothing()
    {
        // A sandboxed or data: document reports "null". No allowlist entry could
        // ever match it, and treating it as a website would be a lie.
        Assert.NotEmpty(Refusal("null"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("//example.com")]
    public void Something_that_is_not_an_address_is_refused(string? candidate)
    {
        Assert.NotEmpty(Refusal(candidate));
    }

    [Fact]
    public void Plain_http_is_refused_for_a_public_website()
    {
        // An http allowlist entry is one an attacker on the same network can
        // impersonate, which would make the allowlist decorative.
        Assert.Contains("https", Refusal("http://www.example.com"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://app.localhost:4200")]
    [InlineData("http://[::1]:5000")]
    public void Plain_http_is_allowed_on_loopback_so_a_site_can_be_built(string candidate)
    {
        WebChatOrigin origin = WebChatOrigin.Create(candidate);

        Assert.True(origin.IsLoopback);
    }

    [Fact]
    public void A_public_website_is_not_loopback()
    {
        Assert.False(WebChatOrigin.Create("https://www.example.com").IsLoopback);
    }

    [Fact]
    public void An_address_longer_than_the_column_is_refused()
    {
        string tooLong = $"https://{new string('a', WebChatOrigin.MaxLength)}.example.com";

        Assert.NotEmpty(Refusal(tooLong));
    }

    [Fact]
    public void Create_throws_where_TryCreate_declines()
    {
        Assert.Throws<DomainValidationException>(() => WebChatOrigin.Create("https://*.example.com"));
    }
}
