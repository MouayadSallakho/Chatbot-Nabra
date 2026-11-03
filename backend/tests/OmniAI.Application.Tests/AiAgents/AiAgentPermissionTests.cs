using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.PreviewAiAgent;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// Which permission controls which AI agent operation.
/// </summary>
/// <remarks>
/// <strong>The point of this suite is what each permission does <em>not</em>
/// buy.</strong> It is easy to write a matrix proving that a permission allows
/// the operation it names; the failure that matters is a permission quietly
/// allowing something else, which is how a workspace ends up granting more than
/// it meant to. So every case checks both directions: the granted operation
/// succeeds, and a neighbouring operation is refused for the same member.
/// <para>
/// Preview-only is the case worth reading closely. A member with just
/// <c>ai-agents.preview</c> can try an agent out and can do nothing else --
/// not list, not read, not rename. The agent id such a member needs comes from
/// test setup, not from granting them the ability to look it up, because
/// granting <c>ai-agents.view</c> to make the test convenient would test a
/// different thing.
/// </para>
/// </remarks>
public sealed class AiAgentPermissionTests
{
    private static readonly UserId Member = UserId.New();

    /// <summary>The workspace these tests act in.</summary>
    /// <remarks>
    /// A real aggregate rather than a bare id, because creating an agent checks
    /// that the tenant exists as well as that the caller may create one. Seeding
    /// a tenant with a different id would make the create case fail as not-found
    /// and quietly stop testing the permission.
    /// </remarks>
    private static readonly Tenant WorkspaceTenant = Tenant.Create(
        TenantName.Create("Acme Corporation"),
        TenantSlug.Create("acme"));

    private static readonly TenantId Workspace = WorkspaceTenant.Id;
    private static readonly DateTimeOffset Created = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(1);

    [Fact]
    public async Task Listing_agents_needs_the_view_permission()
    {
        (InMemoryAiAgentRepository agents, _) = Seed();

        ListAiAgentsHandler allowed = new(agents, With(PlatformPermissions.AiAgentsView), InMemoryAiAgentChannelReader.None);

        Assert.Single((await allowed.HandleAsync(
            new ListAiAgentsQuery(Workspace.Value, null, null))).Items);

        ListAiAgentsHandler refused = new(agents, With(PlatformPermissions.AiAgentsEdit), InMemoryAiAgentChannelReader.None);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => refused.HandleAsync(
            new ListAiAgentsQuery(Workspace.Value, null, null)));
    }

    [Fact]
    public async Task Reading_one_agent_needs_the_view_permission()
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();

        GetAiAgentByIdHandler allowed = new(agents, With(PlatformPermissions.AiAgentsView), InMemoryAiAgentChannelReader.None);

        Assert.NotNull(await allowed.HandleAsync(
            new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value)));

        GetAiAgentByIdHandler refused = new(agents, With(PlatformPermissions.AiAgentsCreate), InMemoryAiAgentChannelReader.None);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => refused.HandleAsync(
            new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value)));
    }

    [Fact]
    public async Task Creating_an_agent_needs_the_create_permission()
    {
        (InMemoryAiAgentRepository agents, _) = Seed();
        InMemoryTenantRepository tenants = SeededTenants();

        CreateAiAgentHandler allowed = new(
            new FakeEntitlements(),
            tenants,
            agents,
            With(PlatformPermissions.AiAgentsCreate),
            new FixedTimeProvider(Later));

        Assert.NotNull(await allowed.HandleAsync(
            new CreateAiAgentCommand(Workspace.Value, "New agent", "Be helpful.")));

        // Being able to edit an existing agent does not let somebody add one --
        // a workspace may reasonably let staff refine agents without letting them
        // proliferate.
        CreateAiAgentHandler refused = new(
            new FakeEntitlements(),
            tenants,
            agents,
            With(PlatformPermissions.AiAgentsEdit),
            new FixedTimeProvider(Later));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => refused.HandleAsync(
            new CreateAiAgentCommand(Workspace.Value, "Another agent", "Be helpful.")));
    }

    [Fact]
    public async Task Renaming_and_editing_instructions_need_the_edit_permission()
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        ConcurrencyToken version = agents.VersionOf(agent);

        RenameAiAgentHandler rename = new(
            agents,
            With(PlatformPermissions.AiAgentsEdit),
            new FixedTimeProvider(Later));

        Assert.NotNull(await rename.HandleAsync(
            new RenameAiAgentCommand(Workspace.Value, agent.Id.Value, "Renamed", version)));

        UpdateAiAgentInstructionsHandler refusedInstructions = new(
            agents,
            With(PlatformPermissions.AiAgentsView),
            new FixedTimeProvider(Later));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => refusedInstructions.HandleAsync(new UpdateAiAgentInstructionsCommand(
                Workspace.Value,
                agent.Id.Value,
                "Different.",
                agents.VersionOf(agent))));
    }

    [Fact]
    public async Task Lifecycle_changes_need_the_lifecycle_permission()
    {
        // Separate from edit because the two are different risks: editing
        // instructions changes what an agent would say, activating one decides
        // whether it says anything to customers at all.
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();

        ActivateAiAgentHandler refused = new(
            agents,
            With(PlatformPermissions.AiAgentsEdit),
            new FixedTimeProvider(Later));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => refused.HandleAsync(
            new ActivateAiAgentCommand(
                Workspace.Value,
                agent.Id.Value,
                agents.VersionOf(agent))));

        ActivateAiAgentHandler allowed = new(
            agents,
            With(PlatformPermissions.AiAgentsLifecycle),
            new FixedTimeProvider(Later));

        Assert.NotNull(await allowed.HandleAsync(new ActivateAiAgentCommand(
            Workspace.Value,
            agent.Id.Value,
            agents.VersionOf(agent))));

        DisableAiAgentHandler disable = new(
            agents,
            With(PlatformPermissions.AiAgentsLifecycle),
            new FixedTimeProvider(Later));

        Assert.NotNull(await disable.HandleAsync(new DisableAiAgentCommand(
            Workspace.Value,
            agent.Id.Value,
            agents.VersionOf(agent))));
    }

    [Fact]
    public async Task Preview_needs_the_preview_permission_and_grants_nothing_else()
    {
        // The escalation check that matters most for this slice. A member holding
        // only ai-agents.preview can try an agent out -- and cannot list agents,
        // cannot read one, and cannot rename one.
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        RecordingAiTextGenerator generator = new();

        TenantAuthorizer previewOnly = With(PlatformPermissions.AiAgentsPreview);

        PreviewAiAgentHandler preview = new(agents, previewOnly, generator);

        Assert.NotNull(await preview.HandleAsync(
            new PreviewAiAgentCommand(Workspace.Value, agent.Id.Value, "Hello")));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => new ListAiAgentsHandler(agents, previewOnly, InMemoryAiAgentChannelReader.None)
                .HandleAsync(new ListAiAgentsQuery(Workspace.Value, null, null)));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => new GetAiAgentByIdHandler(agents, previewOnly, InMemoryAiAgentChannelReader.None)
                .HandleAsync(new GetAiAgentByIdQuery(Workspace.Value, agent.Id.Value)));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => new RenameAiAgentHandler(agents, previewOnly, new FixedTimeProvider(Later))
                .HandleAsync(new RenameAiAgentCommand(
                    Workspace.Value,
                    agent.Id.Value,
                    "Renamed",
                    agents.VersionOf(agent))));
    }

    [Fact]
    public async Task A_member_without_preview_never_reaches_the_generator()
    {
        // The provider is the one place a request costs money, so the refusal has
        // to happen before the generator is constructed into a call -- not after,
        // with the answer withheld.
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        RecordingAiTextGenerator generator = new();

        PreviewAiAgentHandler handler =
            new(agents, With(PlatformPermissions.AiAgentsView), generator);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => handler.HandleAsync(
            new PreviewAiAgentCommand(Workspace.Value, agent.Id.Value, "Hello")));

        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task A_non_member_never_reaches_the_generator_either()
    {
        (InMemoryAiAgentRepository agents, AiAgent agent) = Seed();
        RecordingAiTextGenerator generator = new();

        PreviewAiAgentHandler handler =
            new(agents, TestAuthority.Outsider(Member), generator);

        // Not-found rather than denied, and still nothing sent anywhere.
        Assert.Null(await handler.HandleAsync(
            new PreviewAiAgentCommand(Workspace.Value, agent.Id.Value, "Hello")));

        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Holding_one_agent_permission_never_implies_another()
    {
        // Stated as a whole-catalogue property rather than as a handful of pairs,
        // so it keeps being worth something when the catalogue grows: a member
        // granted exactly one AI agent capability holds exactly that one.
        foreach (PermissionCode granted in PlatformPermissions.AllCodes)
        {
            TenantAuthorizer authorizer = With(granted);

            foreach (PermissionCode other in PlatformPermissions.AllCodes)
            {
                TenantAccessDecision decision =
                    await authorizer.AuthorizeAsync(Workspace, other);

                Assert.Equal(
                    other == granted
                        ? TenantAccessDecision.Granted
                        : TenantAccessDecision.PermissionDenied,
                    decision);
            }
        }
    }

    /// <summary>A member of the workspace holding exactly these permissions.</summary>
    private static TenantAuthorizer With(params PermissionCode[] permissions) =>
        TestAuthority.With(Member, Workspace.Value, permissions);

    /// <summary>One draft agent in the workspace.</summary>
    private static (InMemoryAiAgentRepository Agents, AiAgent Agent) Seed()
    {
        AiAgent agent = AiAgent.Create(
            Workspace,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Answer sales questions."),
            Created);

        return (new InMemoryAiAgentRepository().Seed(agent), agent);
    }

    private static InMemoryTenantRepository SeededTenants() =>
        new InMemoryTenantRepository().Seed(WorkspaceTenant);
}
