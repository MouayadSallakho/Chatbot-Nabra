using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Tenants;

/// <summary>
/// The tenant onboarding slice over HTTP, end to end, against real PostgreSQL.
/// </summary>
public sealed class TenantEndpointsTests(TenantApiFixture api) : IClassFixture<TenantApiFixture>
{
    private const string TenantsRoute = "/api/tenants";
    private const string ProblemJson = "application/problem+json";

    // ------------------------------------------------------------------- create

    [Fact]
    public async Task Creating_a_tenant_returns_201_with_the_created_representation()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage response = await PostAsync(client, "Acme Corporation", slug);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        TenantPayload? created = await ReadTenantAsync(response);

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Acme Corporation", created.Name);
        Assert.Equal(slug, created.Slug);
        Assert.Equal("Active", created.Status);
    }

    [Fact]
    public async Task Creating_a_tenant_sets_a_location_header_pointing_at_the_read_endpoint()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, "Acme Corporation", NewSlug());
        TenantPayload created = await ReadTenantAsync(response) ?? throw new InvalidOperationException();

        Assert.NotNull(response.Headers.Location);

        // CreatedAtRoute emits an absolute URI, which is valid; what matters is
        // that it addresses the read endpoint for this tenant.
        Assert.Equal(
            $"{TenantsRoute}/{created.Id}",
            response.Headers.Location.AbsolutePath,
            ignoreCase: true);

        // The header is only useful if it actually resolves.
        using HttpResponseMessage followed = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    [Fact]
    public async Task A_created_tenant_is_in_the_database()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage response = await PostAsync(client, "Acme Corporation", slug);
        TenantPayload created = await ReadTenantAsync(response) ?? throw new InvalidOperationException();

        // Read straight from PostgreSQL, bypassing the API entirely.
        object? stored = await api.Database.ExecuteScalarAsync(
            "SELECT name || '|' || slug || '|' || status FROM tenants " +
            $"WHERE id = '{created.Id}'");

        Assert.Equal($"Acme Corporation|{slug}|Active", stored as string);
    }

    [Fact]
    public async Task Creation_stores_and_returns_the_domain_canonical_values()
    {
        // A padded name and a mixed-case, padded slug. The Domain canonicalises
        // both, and the response and the row must agree with it -- no silent
        // truncation, and no slug invented from the name.
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage response = await PostAsync(
            client,
            "   Acme Corporation   ",
            $"  {slug.ToUpperInvariant()}  ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        TenantPayload created = await ReadTenantAsync(response) ?? throw new InvalidOperationException();

        Assert.Equal("Acme Corporation", created.Name);
        Assert.Equal(slug, created.Slug);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name || '|' || slug FROM tenants WHERE id = '{created.Id}'");

        Assert.Equal($"Acme Corporation|{slug}", stored as string);
    }

    [Fact]
    public async Task The_caller_cannot_choose_the_id_or_the_status()
    {
        // Extra properties are not part of the contract, so they are ignored
        // rather than honoured. The identity must be generated and the tenant
        // must start Active regardless of what was sent.
        using HttpClient client = api.CreateClient();
        Guid attempted = Guid.CreateVersion7();

        string body = JsonSerializer.Serialize(new
        {
            id = attempted,
            status = "Suspended",
            name = "Acme Corporation",
            slug = NewSlug(),
        });

        using StringContent content = new(body, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(Route(), content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        TenantPayload created = await ReadTenantAsync(response) ?? throw new InvalidOperationException();

        Assert.NotEqual(attempted, created.Id);
        Assert.Equal("Active", created.Status);
    }

    // --------------------------------------------------------------- validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_invalid_name_is_rejected_as_400_problem_details(string? name)
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, name, NewSlug());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        ProblemPayload problem = await ReadProblemAsync(response);

        Assert.Equal(400, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Fact]
    public async Task An_over_long_name_is_rejected_rather_than_truncated()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage response = await PostAsync(client, new string('a', 201), slug);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

        Assert.Equal(0L, stored as long?);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac--me")]
    [InlineData("acme corp")]
    [InlineData("acme_corp")]
    [InlineData("ACME!")]
    public async Task An_invalid_slug_is_rejected_as_400_problem_details(string? slug)
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, "Acme Corporation", slug);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, (await ReadProblemAsync(response)).Status);
    }

    [Fact]
    public async Task A_malformed_body_is_rejected_at_the_transport_layer()
    {
        // Not a Domain concern: this never reaches a value object, because it is
        // not valid JSON of the expected shape.
        using HttpClient client = api.CreateClient();
        using StringContent content = new("{ not json", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync(Route(), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_validation_failure_discloses_no_internals()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await PostAsync(client, "Acme Corporation", "ac--me");

        string body = await response.Content.ReadAsStringAsync();

        AssertNoInternalsLeaked(body);
    }

    // ----------------------------------------------------------------- conflict

    [Fact]
    public async Task A_sequential_duplicate_slug_is_409_problem_details()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage first = await PostAsync(client, "Acme One", slug);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage second = await PostAsync(client, "Acme Two", slug);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ProblemJson, second.Content.Headers.ContentType?.MediaType);

        ProblemPayload problem = await ReadProblemAsync(second);

        Assert.Equal(409, problem.Status);

        AssertNoInternalsLeaked(await second.Content.ReadAsStringAsync());

        object? rows = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

        Assert.Equal(1L, rows as long?);
    }

    [Fact]
    public async Task A_duplicate_slug_differing_only_in_case_is_still_a_conflict()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage first = await PostAsync(client, "Acme One", slug);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using HttpResponseMessage second = await PostAsync(client, "Acme Two", slug.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Two_concurrent_duplicate_creations_produce_exactly_one_201_and_one_409()
    {
        // The race the brief calls out. Nothing in application code serialises
        // these: no lock, no mutex, no serialisable transaction. Exactly one
        // request wins because PostgreSQL's unique index says so.
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        HttpResponseMessage[] responses = await Task.WhenAll(
            PostAsync(client, "Acme One", slug),
            PostAsync(client, "Acme Two", slug));

        try
        {
            HttpStatusCode[] codes = [.. responses.Select(response => response.StatusCode)];

            Assert.Equal(1, codes.Count(code => code == HttpStatusCode.Created));
            Assert.Equal(1, codes.Count(code => code == HttpStatusCode.Conflict));

            // No 500: a unique-violation race must never surface as a server fault.
            Assert.DoesNotContain(HttpStatusCode.InternalServerError, codes);

            object? rows = await api.Database.ExecuteScalarAsync(
                $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

            Assert.Equal(1L, rows as long?);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Eight_concurrent_duplicate_creations_still_produce_exactly_one_tenant()
    {
        // Widened so the insert path, not merely the advisory pre-check, is the
        // thing doing the rejecting: with eight simultaneous requests, several
        // reliably pass the pre-check before any of them commits.
        const int callers = 8;

        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, callers).Select(index => PostAsync(client, $"Acme {index}", slug)));

        try
        {
            HttpStatusCode[] codes = [.. responses.Select(response => response.StatusCode)];

            Assert.Equal(1, codes.Count(code => code == HttpStatusCode.Created));
            Assert.Equal(callers - 1, codes.Count(code => code == HttpStatusCode.Conflict));
            Assert.DoesNotContain(HttpStatusCode.InternalServerError, codes);

            object? rows = await api.Database.ExecuteScalarAsync(
                $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

            Assert.Equal(1L, rows as long?);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    // ---------------------------------------------------------------------- get

    [Fact]
    public async Task Getting_an_existing_tenant_returns_200_with_the_correct_payload()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage creation = await PostAsync(client, "Acme Corporation", slug);
        TenantPayload created = await ReadTenantAsync(creation) ?? throw new InvalidOperationException();

        using HttpResponseMessage response = await client.GetAsync(Route(created.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TenantPayload? found = await ReadTenantAsync(response);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("Acme Corporation", found.Name);
        Assert.Equal(slug, found.Slug);
        Assert.Equal("Active", found.Status);
    }

    [Fact]
    public async Task The_status_is_a_name_on_the_wire_never_an_enum_ordinal()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage creation = await PostAsync(client, "Acme Corporation", NewSlug());
        string body = await creation.Content.ReadAsStringAsync();

        // Asserted against the raw JSON, because a deserialised value would hide
        // the difference between "Active" and 1.
        Assert.Contains("\"status\":\"Active\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Getting_an_unknown_tenant_returns_404_problem_details()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        ProblemPayload problem = await ReadProblemAsync(response);

        Assert.Equal(404, problem.Status);

        AssertNoInternalsLeaked(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_empty_tenant_id_is_rejected_as_400_rather_than_reported_as_missing()
    {
        // An all-zero GUID matches the route constraint but is not a valid
        // identity, so it is invalid input rather than a tenant that is absent.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_non_guid_tenant_id_does_not_match_the_route()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync(new Uri($"{TenantsRoute}/not-a-guid", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --------------------------------------------------------------- round trip

    [Fact]
    public async Task A_created_tenant_round_trips_through_its_location_header()
    {
        using HttpClient client = api.CreateClient();
        string slug = NewSlug();

        using HttpResponseMessage creation = await PostAsync(client, "Acme Corporation", slug);

        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);

        TenantPayload created = await ReadTenantAsync(creation) ?? throw new InvalidOperationException();
        Uri location = creation.Headers.Location ?? throw new InvalidOperationException("No Location header.");

        using HttpResponseMessage read = await client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        TenantPayload found = await ReadTenantAsync(read) ?? throw new InvalidOperationException();

        // The same logical tenant, field for field.
        Assert.Equal(created, found);
    }

    [Fact]
    public async Task Two_tenants_with_different_slugs_are_both_created_and_readable()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage firstCreation = await PostAsync(client, "Acme", NewSlug());
        using HttpResponseMessage secondCreation = await PostAsync(client, "Globex", NewSlug());

        Assert.Equal(HttpStatusCode.Created, firstCreation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondCreation.StatusCode);

        TenantPayload first = await ReadTenantAsync(firstCreation) ?? throw new InvalidOperationException();
        TenantPayload second = await ReadTenantAsync(secondCreation) ?? throw new InvalidOperationException();

        Assert.NotEqual(first.Id, second.Id);

        using HttpResponseMessage readFirst = await client.GetAsync(Route(first.Id));
        using HttpResponseMessage readSecond = await client.GetAsync(Route(second.Id));

        Assert.Equal("Acme", (await ReadTenantAsync(readFirst))?.Name);
        Assert.Equal("Globex", (await ReadTenantAsync(readSecond))?.Name);
    }

    // ------------------------------------------------------------------ harness

    [Fact]
    public void The_hosted_api_is_using_the_disposable_test_database()
    {
        // Guards the suite itself: if the connection string were not reaching the
        // application, every persistence assertion above would be meaningless.
        string resolved = api.ResolvedConnectionString();

        Assert.Contains("omniai_test_", resolved, StringComparison.Ordinal);
    }

    private static Uri Route() => new(TenantsRoute, UriKind.Relative);

    private static Uri Route(Guid tenantId) =>
        new($"{TenantsRoute}/{tenantId.ToString("D", CultureInfo.InvariantCulture)}", UriKind.Relative);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string? name, string? slug) =>
        client.PostAsJsonAsync(Route(), new { name, slug });

    private static async Task<TenantPayload?> ReadTenantAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<TenantPayload>();

    private static async Task<ProblemPayload> ReadProblemAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ProblemPayload>()
        ?? throw new InvalidOperationException("The response carried no Problem Details body.");

    /// <summary>
    /// Asserts a response body carries no database, provider or framework detail.
    /// </summary>
    private static void AssertNoInternalsLeaked(string body)
    {
        foreach (string leak in (string[])
        [
            "ix_tenants_slug", "23505", "duplicate key", "Npgsql", "PostgresException",
            "DbUpdateException", "INSERT INTO", "SELECT ", "stackTrace", "   at ",
        ])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A slug unique to one test, so tests cannot collide with each other.</summary>
    private static string NewSlug() => $"t{Guid.NewGuid():N}"[..20];

    private sealed record TenantPayload(Guid Id, string Name, string Slug, string Status);

    private sealed record ProblemPayload(string? Type, string? Title, int? Status, string? Detail);
}
