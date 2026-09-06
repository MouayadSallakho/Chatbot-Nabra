using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// A hosted API with the ability to arrange restricted members.
/// </summary>
/// <remarks>
/// <strong>Restricted members are arranged by writing rows, never by calling a
/// production endpoint.</strong> There is no member-assignment API in this
/// platform and this fixture does not invent one: it opens the test database and
/// inserts a role and an assignment directly, which is test infrastructure rather
/// than product surface. Adding a <c>/api/dev/grant-role</c> endpoint to make
/// these tests convenient would be an authorization backdoor shipped in the
/// application, which is precisely what must not exist.
/// <para>
/// Everything else goes through the real HTTP pipeline: accounts are created
/// through the registration endpoint, workspaces through the tenant endpoint, and
/// every assertion is about what the API answers.
/// </para>
/// </remarks>
public sealed class RoleApiFixture : AiAgentApiFixture
{
    /// <summary>
    /// Registers an account, gives it a workspace, and returns both.
    /// </summary>
    /// <remarks>
    /// The workspace is created through <c>POST /api/tenants</c>, so the Owner
    /// role and its assignment are written by production provisioning rather than
    /// by this fixture. That matters: a test that hand-seeded them could pass
    /// while provisioning was broken.
    /// </remarks>
    public async Task<(HttpClient Client, Guid TenantId, Guid UserId)> CreateOwnerAsync(
        string name = "Acme Trading")
    {
        HttpClient client = await CreateSignedInClientAsync();

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name, slug = $"t{Guid.NewGuid():N}"[..20] });

        created.EnsureSuccessStatusCode();

        using JsonDocument body =
            JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            client,
            body.RootElement.GetProperty("id").GetGuid(),
            await CurrentUserIdAsync(client));
    }

    /// <summary>
    /// Registers a second account, makes it a member of an existing workspace,
    /// and gives it a custom role granting exactly these permissions.
    /// </summary>
    /// <remarks>
    /// The membership and the assignment are written directly, because no
    /// endpoint grants either. The role itself is a perfectly ordinary custom
    /// role -- the same shape <c>POST /roles</c> produces -- so what these tests
    /// exercise is a real restricted member rather than a special case the
    /// application knows about.
    /// </remarks>
    public async Task<HttpClient> CreateRestrictedMemberAsync(
        Guid tenantId,
        params string[] permissions)
    {
        (HttpClient client, _) =
            await CreateRestrictedMemberWithRoleAsync(tenantId, permissions);

        return client;
    }

    /// <summary>
    /// The same restricted member, and the id of the role they hold.
    /// </summary>
    /// <remarks>
    /// The role id is what makes self-escalation testable: adding a permission to
    /// a role assigned to the caller is the one edit that changes the caller's own
    /// authority, and a test cannot attempt it without naming that role.
    /// </remarks>
    public async Task<(HttpClient Client, Guid RoleId)> CreateRestrictedMemberWithRoleAsync(
        Guid tenantId,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        HttpClient client = await CreateSignedInClientAsync();
        Guid userId = await CurrentUserIdAsync(client);

        TenantRole role = TenantRole.CreateCustom(
            TenantId.From(tenantId),
            TenantRoleName.Create($"Limited {Guid.NewGuid():N}"[..20]),
            [.. permissions.Select(PermissionCode.Create)],
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = CreateDbContext();

        context.TenantMemberships.Add(TenantMembership.Create(
            UserId.From(userId),
            TenantId.From(tenantId),
            DateTimeOffset.UtcNow));

        context.TenantRoles.Add(role);
        context.Entry(role)
            .Property<string>("NormalisedName")
            .CurrentValue = role.Name.Normalised;

        await context.SaveChangesAsync();

        // A second save, because the assignment's composite foreign keys require
        // both the membership and the role to be committed first.
        await using OmniAiDbContext assigning = CreateDbContext();

        assigning.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(
            UserId.From(userId),
            role,
            DateTimeOffset.UtcNow));

        await assigning.SaveChangesAsync();

        return (client, role.Id.Value);
    }

    /// <summary>
    /// Registers a second account with a workspace of its own.
    /// </summary>
    /// <remarks>
    /// The "other tenant" caller. They are a legitimate user of the platform with
    /// full authority somewhere else, which is what makes their 404 against this
    /// workspace meaningful.
    /// </remarks>
    public async Task<HttpClient> CreateOtherTenantUserAsync()
    {
        (HttpClient client, _, _) = await CreateOwnerAsync("Rival Trading");

        return client;
    }
}
