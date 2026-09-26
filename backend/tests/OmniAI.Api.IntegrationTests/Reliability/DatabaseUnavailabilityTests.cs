using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using OmniAI.Api.ErrorHandling;

namespace OmniAI.Api.IntegrationTests.Reliability;

/// <summary>
/// Phase 34: an unavailable database is reported as 503, never as a server bug,
/// and never at the cost of hiding a defect.
/// </summary>
/// <remarks>
/// Found under load: with more concurrent turns than PostgreSQL would take
/// connections for, every refused request answered 500 (<c>53300</c>, and later
/// "connection pool has been exhausted"). The durable state stayed consistent;
/// the answer did not tell the truth about why.
/// </remarks>
public sealed class DatabaseUnavailabilityTests
{
    [Theory]
    [InlineData("53300")] // too_many_connections
    [InlineData("53200")] // out_of_memory
    [InlineData("08006")] // connection_failure
    [InlineData("57P03")] // cannot_connect_now
    [InlineData("57P01")] // admin_shutdown
    public void Connection_level_server_states_are_unavailability(string sqlState)
    {
        Assert.True(DatabaseUnavailability.Recognises(Server(sqlState), out string reason));
        Assert.Contains(sqlState, reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("40P01")] // deadlock_detected -- can be the code's fault
    [InlineData("40001")] // serialization_failure
    [InlineData("57014")] // query_canceled
    [InlineData("23505")] // unique_violation
    [InlineData("42P01")] // undefined_table
    public void Errors_the_code_can_cause_are_not_disguised_as_unavailability(string sqlState)
    {
        Assert.False(DatabaseUnavailability.Recognises(Server(sqlState), out _));
        Assert.False(DatabaseUnavailability.Recognises(new InvalidOperationException("wrapped", Server(sqlState)), out _));
    }

    [Fact]
    public void Wrapped_exhaustion_timeouts_and_refused_connections_are_recognised()
    {
        // EF wraps a transient failure it has no retry strategy for.
        Assert.True(DatabaseUnavailability.Recognises(
            new InvalidOperationException("transient", Server("53300")), out _));

        // Pool exhausted / connect timeout: Npgsql's own exception over a timeout.
        Assert.True(DatabaseUnavailability.Recognises(
            new NpgsqlException("The connection pool has been exhausted", new TimeoutException()), out _));

        // Refused connection.
        Assert.True(DatabaseUnavailability.Recognises(
            new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)), out _));
    }

    [Fact]
    public void Unrelated_failures_are_left_to_the_default_handler()
    {
        Assert.False(DatabaseUnavailability.Recognises(new InvalidOperationException("bug"), out _));
        Assert.False(DatabaseUnavailability.Recognises(new NpgsqlException("protocol"), out _));
        Assert.False(DatabaseUnavailability.Recognises(new TimeoutException(), out _));
    }

    [Fact]
    public async Task A_request_that_needs_an_unreachable_database_answers_503_and_names_nothing()
    {
        await using WebApplicationFactory<Program> broken = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:OmniAiDatabase",
                    "Host=127.0.0.1;Port=1;Username=canaryuser;Password=canary-db-password;Database=canarydb;Timeout=2");
                builder.UseSetting("OPENAI_API_KEY", string.Empty);
            });

        using HttpClient client = broken.CreateClient();

        // Anonymous, read-only, and a well-formed key (22 base64url characters) has
        // to be looked up in the database.
        using HttpResponseMessage response = await client.GetAsync(
            new Uri($"/api/public/webchat/{Guid.NewGuid():N}"[..(20 + 22)], UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal("Service temporarily unavailable", problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));

        // Nothing about the database reaches the caller.
        foreach (string secret in new[] { "canary", "127.0.0.1", "Port", "SQLSTATE", "Npgsql", "53300", "08001" })
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PostgresException Server(string sqlState) =>
        new("synthetic", "ERROR", "ERROR", sqlState);
}
