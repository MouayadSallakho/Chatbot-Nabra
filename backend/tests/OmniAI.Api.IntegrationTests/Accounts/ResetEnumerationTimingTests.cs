using System.Diagnostics;
using System.Globalization;
using System.Net;
using Xunit.Abstractions;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// A reset request takes as long for an address with an account as for one without
/// (Phase 37.3): at least 100 requests per case, interleaved, compared by their
/// distributions.
/// </summary>
/// <remarks>
/// Enumeration by timing can only be bounded, not proven absent. What is asserted
/// is that both cases sit on the padded floor and that their medians and 90th
/// percentiles differ by less than the noise allowance; the numbers are written to
/// the test output for the report.
/// </remarks>
public sealed class ResetEnumerationTimingTests(AccountApiFixture api, ITestOutputHelper output) : IClassFixture<AccountApiFixture>
{
    private const int PerCase = 120;

    /// <summary>The configured floor every answer is padded to.</summary>
    private static readonly TimeSpan Floor = TimeSpan.FromMilliseconds(400);

    /// <summary>How far apart the two cases may sit and still be noise.</summary>
    private static readonly TimeSpan Noise = TimeSpan.FromMilliseconds(40);

    [Fact]
    public async Task Known_and_unknown_addresses_take_the_same_time_within_noise()
    {
        string known = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(owner, known);
        }

        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        // Warm the path once each, so neither case pays for first use.
        (await AccountApiFixture.ForgotAsync(client, known)).Dispose();
        (await AccountApiFixture.ForgotAsync(client, ApiAuthentication.UniqueEmail())).Dispose();

        List<double> knownTimes = [];
        List<double> unknownTimes = [];

        for (int round = 0; round < PerCase; round++)
        {
            // Alternating which goes first, so drift in the host affects both.
            bool knownFirst = round % 2 == 0;

            foreach (bool isKnown in knownFirst ? (bool[])[true, false] : [false, true])
            {
                string address = isKnown ? known : ApiAuthentication.UniqueEmail();
                long started = Stopwatch.GetTimestamp();

                using HttpResponseMessage answer = await AccountApiFixture.ForgotAsync(client, address);

                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode);
                (isKnown ? knownTimes : unknownTimes).Add(elapsed);
            }
        }

        Summary knownSummary = Summarise(knownTimes);
        Summary unknownSummary = Summarise(unknownTimes);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"known   n={knownTimes.Count} {knownSummary}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"unknown n={unknownTimes.Count} {unknownSummary}"));

        string observed = $"known {knownSummary}; unknown {unknownSummary}";

        Assert.True(knownSummary.Min >= Floor.TotalMilliseconds - 1, "Known requests answered before the floor: " + observed);
        Assert.True(unknownSummary.Min >= Floor.TotalMilliseconds - 1, "Unknown requests answered before the floor: " + observed);
        Assert.True(Math.Abs(knownSummary.Median - unknownSummary.Median) <= Noise.TotalMilliseconds, "Medians differ beyond noise: " + observed);
        Assert.True(Math.Abs(knownSummary.P90 - unknownSummary.P90) <= Noise.TotalMilliseconds, "90th percentiles differ beyond noise: " + observed);
    }

    private static Summary Summarise(List<double> times)
    {
        double[] sorted = [.. times.Order()];

        return new Summary(
            sorted[0],
            Percentile(sorted, 0.5),
            Percentile(sorted, 0.9),
            Percentile(sorted, 0.95),
            sorted[^1]);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed record Summary(double Min, double Median, double P90, double P95, double Max)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"min={Min:F1} median={Median:F1} p90={P90:F1} p95={P95:F1} max={Max:F1} ms");
    }
}
