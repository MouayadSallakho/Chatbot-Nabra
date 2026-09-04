using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Tenants;

public sealed class CreateTenantHandlerTests
{
    [Fact]
    public async Task A_valid_command_creates_the_tenant()
    {
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        TenantView created = await handler.HandleAsync(
            new CreateTenantCommand("Acme Corporation", "acme"));

        Assert.Equal("Acme Corporation", created.Name);
        Assert.Equal("acme", created.Slug);
        Assert.Single(tenants.Stored);
        Assert.Equal(created.Id, tenants.Stored[0].Id.Value);
    }

    [Fact]
    public async Task The_name_is_stored_as_the_domain_normalises_it()
    {
        // The handler must not do its own trimming: the Domain owns the rule, and
        // the value that reaches storage is the Domain's canonical form.
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        TenantView created = await handler.HandleAsync(
            new CreateTenantCommand("   Acme Corporation   ", "acme"));

        Assert.Equal("Acme Corporation", created.Name);
        Assert.Equal("Acme Corporation", tenants.Stored[0].Name.Value);
    }

    [Fact]
    public async Task The_slug_is_stored_as_the_domain_canonicalises_it()
    {
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        TenantView created = await handler.HandleAsync(
            new CreateTenantCommand("Acme Corporation", "  ACME-Syria  "));

        Assert.Equal("acme-syria", created.Slug);
        Assert.Equal("acme-syria", tenants.Stored[0].Slug.Value);
    }

    [Fact]
    public async Task A_new_tenant_starts_active()
    {
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        TenantView created = await handler.HandleAsync(
            new CreateTenantCommand("Acme Corporation", "acme"));

        Assert.Equal(TenantStatus.Active, created.Status);
        Assert.Equal(TenantStatus.Active, tenants.Stored[0].Status);
    }

    [Fact]
    public async Task The_identity_is_generated_and_never_empty()
    {
        // The command carries no id, so the aggregate is the only thing that can
        // have supplied this one.
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        TenantView first = await handler.HandleAsync(new CreateTenantCommand("Acme", "acme"));
        TenantView second = await handler.HandleAsync(new CreateTenantCommand("Globex", "globex"));

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(tenants.Stored[0].Id.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_rejected_by_the_domain(string? name)
    {
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new CreateTenantCommand(name, "acme")));

        Assert.Empty(tenants.Stored);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac--me")]
    [InlineData("acme corp")]
    [InlineData("acme_corp")]
    public async Task An_invalid_slug_is_rejected_by_the_domain(string slug)
    {
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new CreateTenantCommand("Acme Corporation", slug)));

        Assert.Empty(tenants.Stored);
    }

    [Fact]
    public async Task Validation_happens_before_the_store_is_touched()
    {
        // Nothing should reach persistence when the input could never be valid.
        InMemoryTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new CreateTenantCommand("Acme", "no")));

        Assert.Equal(0, tenants.AddAttempts);
    }

    [Fact]
    public async Task A_slug_that_is_already_taken_is_a_conflict()
    {
        InMemoryTenantRepository tenants = new InMemoryTenantRepository()
            .Seed(Tenant.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme")));

        CreateTenantHandler handler = new(tenants);

        TenantSlugAlreadyExistsException conflict =
            await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
                () => handler.HandleAsync(new CreateTenantCommand("Acme Two", "acme")));

        Assert.Equal("acme", conflict.Slug);
        Assert.Single(tenants.Stored);
    }

    [Fact]
    public async Task A_taken_slug_is_detected_case_insensitively()
    {
        // Because the Domain lowercases the slug, "ACME" and "acme" cannot become
        // two tenants.
        InMemoryTenantRepository tenants = new InMemoryTenantRepository()
            .Seed(Tenant.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme")));

        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
            () => handler.HandleAsync(new CreateTenantCommand("Acme Two", "ACME")));
    }

    [Fact]
    public async Task The_store_is_the_authority_when_the_early_check_misses_the_conflict()
    {
        // The race the advisory check cannot win: both callers were told the slug
        // was free. The handler must still surface the store's rejection rather
        // than assume its own check was decisive.
        InMemoryTenantRepository tenants = new InMemoryTenantRepository { PretendSlugIsFree = true };
        tenants.Seed(Tenant.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme")));

        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
            () => handler.HandleAsync(new CreateTenantCommand("Acme Two", "acme")));

        // It genuinely got past the advisory check and reached the store.
        Assert.Equal(1, tenants.AddAttempts);
        Assert.Single(tenants.Stored);
    }

    [Fact]
    public async Task A_null_command_is_a_programming_error()
    {
        CreateTenantHandler handler = new(new InMemoryTenantRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }

    [Fact]
    public void The_handler_requires_a_repository()
    {
        Assert.Throws<ArgumentNullException>(() => new CreateTenantHandler(null!));
    }

    [Fact]
    public async Task A_cancelled_token_is_honoured()
    {
        // The token must reach the store rather than be accepted and ignored.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        CancellationObservingTenantRepository tenants = new();
        CreateTenantHandler handler = new(tenants);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.HandleAsync(new CreateTenantCommand("Acme", "acme"), cancellation.Token));

        Assert.True(tenants.SawToken);
    }

    /// <summary>A repository that respects the token it is handed.</summary>
    private sealed class CancellationObservingTenantRepository : ITenantRepository
    {
        public bool SawToken { get; private set; }

        public Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken cancellationToken = default)
        {
            SawToken = true;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<Tenant?>(null);
        }

        public Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken cancellationToken = default)
        {
            SawToken = true;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(false);
        }

        public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            SawToken = true;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }
}
