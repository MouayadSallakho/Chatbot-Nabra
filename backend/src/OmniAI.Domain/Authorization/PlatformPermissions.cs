using System.Collections.Frozen;

namespace OmniAI.Domain.Authorization;

/// <summary>
/// Every capability this platform recognises, written out.
/// </summary>
/// <remarks>
/// <strong>This list is the product's definition of what can be done.</strong> A
/// tenant administrator composes roles from it and cannot add to it: permissions
/// are platform capabilities, not tenant-authored records. There is no endpoint
/// that creates one, no table that stores one, and no way for a caller to invent
/// a permission string that this platform will then honour -- an unrecognised
/// code is refused at the boundary and never becomes a stored grant.
/// <para>
/// <strong>Compile-time, and deliberately not discovered.</strong> No reflection
/// scans handlers, no attribute is read, and no route table is walked. A
/// discovered catalogue would change silently when a class was renamed or an
/// endpoint was moved, which means the set of things a role can grant would
/// change without anybody deciding to change it. Adding a capability here is an
/// edit somebody makes on purpose and a reviewer can see.
/// </para>
/// <para>
/// <strong>It contains only capabilities that exist today.</strong> There is no
/// <c>conversations.view</c>, no <c>knowledge.manage</c>, no
/// <c>channels.configure</c>, no <c>billing.*</c>, no <c>integrations.*</c> and
/// no <c>analytics.*</c>, because none of those product areas is implemented.
/// Listing them would let an administrator grant authority over nothing, then
/// wonder why it had no effect -- and would quietly commit this platform to
/// whatever shape the guess happened to take. Each arrives with the use cases it
/// governs.
/// </para>
/// <para>
/// <strong>There is no <c>tenant.admin</c>, and that omission is the design.</strong>
/// A single catch-all permission would make every fine-grained code beside it
/// decorative: a role would be granted the one that covers everything, and the
/// distinctions this catalogue draws would never be exercised. Full authority
/// over a workspace is expressed by the Owner system role, which is protected and
/// cannot be assembled from checkboxes.
/// </para>
/// <para>
/// <strong>There is no implication between codes either.</strong>
/// <c>roles.manage</c> does not confer <c>roles.view</c>; <c>ai-agents.edit</c>
/// does not confer <c>ai-agents.view</c>. Union of the member's roles is the only
/// composition rule in this platform. An implication graph would mean the
/// permissions a role actually confers could no longer be read off the role -- a
/// reviewer would have to evaluate a closure in their head -- and it is exactly
/// the kind of machinery that becomes load-bearing before anybody has agreed what
/// it means. A screen that needs two capabilities asks for both, visibly.
/// </para>
/// </remarks>
public static class PlatformPermissions
{
    /// <summary>Reading the workspace itself -- its name, slug and status.</summary>
    public static readonly PermissionCode WorkspaceView = PermissionCode.Create("workspace.view");

    /// <summary>Seeing the tenant's AI agents: the list, and one agent's detail.</summary>
    public static readonly PermissionCode AiAgentsView = PermissionCode.Create("ai-agents.view");

    /// <summary>Creating a new AI agent.</summary>
    public static readonly PermissionCode AiAgentsCreate = PermissionCode.Create("ai-agents.create");

    /// <summary>Changing an agent's name or its instructions.</summary>
    public static readonly PermissionCode AiAgentsEdit = PermissionCode.Create("ai-agents.edit");

    /// <summary>Activating or disabling an agent.</summary>
    /// <remarks>
    /// Separate from <see cref="AiAgentsEdit"/> because the two are different
    /// risks. Editing instructions changes what an agent would say; activating one
    /// decides whether it says anything to customers at all. A workspace may
    /// reasonably let somebody draft agents without letting them put one live.
    /// </remarks>
    public static readonly PermissionCode AiAgentsLifecycle =
        PermissionCode.Create("ai-agents.lifecycle");

    /// <summary>Trying an agent out with a test message.</summary>
    /// <remarks>
    /// Its own permission because it is the one capability in this catalogue that
    /// reaches a paid third party. A workspace that wants to bound provider spend
    /// needs to be able to withhold exactly this, without also withholding the
    /// ability to read an agent's configuration.
    /// </remarks>
    public static readonly PermissionCode AiAgentsPreview =
        PermissionCode.Create("ai-agents.preview");

    /// <summary>Reading the workspace's roles and the permission catalogue.</summary>
    public static readonly PermissionCode RolesView = PermissionCode.Create("roles.view");

    /// <summary>Creating and editing the workspace's custom roles.</summary>
    /// <remarks>
    /// This is the most powerful capability a custom role can hold, because
    /// somebody who has it can compose any other permission into a role. What
    /// stops that from being an escalation is the delegation ceiling: a role may
    /// only be given permissions its editor already holds, which is enforced for
    /// role editing and, from Phase 14, for handing roles to people as well.
    /// </remarks>
    public static readonly PermissionCode RolesManage = PermissionCode.Create("roles.manage");

    /// <summary>Reading the workspace's members and its pending invitations.</summary>
    public static readonly PermissionCode MembersView = PermissionCode.Create("members.view");

    /// <summary>Inviting somebody to the workspace, and revoking an invitation.</summary>
    /// <remarks>
    /// On its own this invites somebody with no capabilities at all: they become a
    /// member and can do nothing until an administrator gives them a role. Issuing
    /// an invitation that carries roles additionally requires
    /// <see cref="MembersRolesManage"/>, because that is what actually hands out
    /// authority.
    /// </remarks>
    public static readonly PermissionCode MembersInvite = PermissionCode.Create("members.invite");

    /// <summary>Changing which roles a member of the workspace holds.</summary>
    /// <remarks>
    /// The capability that turns a role definition into somebody's real authority.
    /// It is bounded by the same ceiling as role editing: the holder may only give
    /// away, and only take away, what they themselves can do.
    /// </remarks>
    public static readonly PermissionCode MembersRolesManage =
        PermissionCode.Create("members.roles.manage");

    /// <summary>Reading the workspace's customer conversations.</summary>
    /// <remarks>
    /// A conversation is the most sensitive business data this platform holds: it
    /// is what a customer actually said, in their own words, to a business they
    /// were probably not expecting to be read by everybody who works there. So
    /// reading them is its own capability rather than something the workspace
    /// permission or an agent permission carries along.
    /// <para>
    /// It grants reading only. Replying, assigning, exporting and deleting are
    /// deliberately absent from the catalogue because none of those operations
    /// exists yet, and a permission that grants nothing today would be a promise
    /// this platform has not made.
    /// </para>
    /// </remarks>
    public static readonly PermissionCode ConversationsView =
        PermissionCode.Create("conversations.view");

    /// <summary>Reading how the workspace's website chat is configured.</summary>
    public static readonly PermissionCode WebsiteChatView =
        PermissionCode.Create("website-chat.view");

    /// <summary>Creating website chat endpoints and taking them in and out of service.</summary>
    /// <remarks>
    /// This is what publishes a workspace's AI to the public internet, and what
    /// withdraws it again. Separate from <see cref="WebsiteChatView"/> because
    /// seeing that a chat endpoint exists and deciding whether the world can talk
    /// to it are different responsibilities — and because activating one starts
    /// spending the workspace's provider budget on whoever finds the link.
    /// </remarks>
    public static readonly PermissionCode WebsiteChatManage =
        PermissionCode.Create("website-chat.manage");

    /// <summary>Read the workspace's knowledge and try retrieval out.</summary>
    public static readonly PermissionCode KnowledgeView = PermissionCode.Create("knowledge.view");

    /// <summary>Write knowledge, index it, switch it on and off, and decide which
    /// agents may draw on it.</summary>
    public static readonly PermissionCode KnowledgeManage =
        PermissionCode.Create("knowledge.manage");

    /// <summary>The workspace group's display name.</summary>
    public const string WorkspaceGroup = "Workspace";

    /// <summary>The AI agent group's display name.</summary>
    public const string AiAgentsGroup = "AI agents";

    /// <summary>The role administration group's display name.</summary>
    public const string RolesGroup = "Roles and permissions";

    /// <summary>The team administration group's display name.</summary>
    public const string MembersGroup = "Team members";

    /// <summary>The conversations group's display name.</summary>
    public const string ConversationsGroup = "Conversations";

    /// <summary>The channel administration group's display name.</summary>
    public const string ChannelsGroup = "Channels";

    public const string KnowledgeGroup = "Knowledge";

    /// <summary>
    /// The catalogue, in the order it is published and rendered.
    /// </summary>
    /// <remarks>
    /// Grouped, and within a group ordered from the least to the most authority,
    /// so a role editor reads top to bottom as an escalation rather than as an
    /// alphabetised jumble. The order is part of what this type publishes: every
    /// list of permissions the API returns is derived from it, so two requests
    /// that grant the same thing produce the same bytes.
    /// </remarks>
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(
            WorkspaceView,
            WorkspaceGroup,
            "View workspace",
            "See the workspace's name and status."),
        new(
            AiAgentsView,
            AiAgentsGroup,
            "View AI agents",
            "See the workspace's AI agents and read their instructions."),
        new(
            AiAgentsCreate,
            AiAgentsGroup,
            "Create AI agents",
            "Add a new AI agent to the workspace."),
        new(
            AiAgentsEdit,
            AiAgentsGroup,
            "Edit AI agents",
            "Change an AI agent's name and instructions."),
        new(
            AiAgentsLifecycle,
            AiAgentsGroup,
            "Activate and disable AI agents",
            "Decide whether an AI agent is live or taken out of service."),
        new(
            AiAgentsPreview,
            AiAgentsGroup,
            "Preview AI agents",
            "Try an AI agent out with a test message. This is the only capability "
            + "that sends a request to the AI provider."),
        new(
            RolesView,
            RolesGroup,
            "View roles",
            "See the workspace's roles and which permissions each one grants."),
        new(
            RolesManage,
            RolesGroup,
            "Manage roles",
            "Create custom roles and change their names and permissions. It does not "
            + "give roles to members."),
        new(
            MembersView,
            MembersGroup,
            "View team members",
            "See who belongs to this workspace, which roles they hold, and which "
            + "invitations are still open."),
        new(
            MembersInvite,
            MembersGroup,
            "Invite team members",
            "Create and revoke invitations. On its own it invites somebody who can "
            + "do nothing until they are given a role."),
        new(
            MembersRolesManage,
            MembersGroup,
            "Assign roles to team members",
            "Decide which roles a member holds. You can only give away, and only "
            + "take away, what your own roles let you do."),
        new(
            ConversationsView,
            ConversationsGroup,
            "View conversations",
            "Read the conversations customers have had with this workspace, "
            + "including everything they wrote."),
        new(
            WebsiteChatView,
            ChannelsGroup,
            "View website chat",
            "See how this workspace's website chat is set up and which agent "
            + "answers on it."),
        new(
            WebsiteChatManage,
            ChannelsGroup,
            "Manage website chat",
            "Create website chat links and decide whether the public can use "
            + "them."),
        new(
            KnowledgeView,
            KnowledgeGroup,
            "View knowledge",
            "Read the workspace's knowledge documents, see whether each one is "
            + "indexed, and test what an agent would retrieve for a question."),
        new(
            KnowledgeManage,
            KnowledgeGroup,
            "Manage knowledge",
            "Write and edit knowledge documents, index them, switch them on and "
            + "off, and choose which AI agents may draw on them. Indexing sends "
            + "the document's text to the AI provider and costs money."),
    ];

    /// <summary>
    /// Every recognised code, in catalogue order.
    /// </summary>
    /// <remarks>
    /// <strong>This is what the Owner role grants, and it is why Owner cannot fall
    /// behind.</strong> Owner holds no permission rows at all: its authority is
    /// this list, read at the moment the question is asked. Adding a capability to
    /// <see cref="All"/> therefore extends every Owner in every tenant in the same
    /// edit, with no migration, no backfill and no row that somebody could forget
    /// to write. See <c>TenantRole</c> for the aggregate that relies on it.
    /// </remarks>
    public static readonly IReadOnlyList<PermissionCode> AllCodes =
        [.. All.Select(definition => definition.Code)];

    /// <summary>
    /// The recognised codes as a set, for membership tests.
    /// </summary>
    /// <remarks>
    /// Frozen because it is built once and read on every grant validation.
    /// <c>FrozenSet</c> is the BCL's own answer for a set that is never mutated
    /// after construction, so no third-party collection package is involved.
    /// </remarks>
    private static readonly FrozenSet<PermissionCode> Recognised = AllCodes.ToFrozenSet();

    /// <summary>
    /// Whether this platform has a capability by this name.
    /// </summary>
    /// <remarks>
    /// The single question every grant is checked against. A code that is
    /// well-formed but unknown -- a value invented by a caller, or one retired
    /// from a previous release -- answers <see langword="false"/> here and is
    /// refused before it can be stored.
    /// </remarks>
    public static bool IsRecognised(PermissionCode code) => Recognised.Contains(code);

    /// <summary>
    /// Requires every code in a set to be one this platform defines.
    /// </summary>
    /// <remarks>
    /// <strong>One definition of "this is not a capability".</strong> The
    /// aggregate needs it to keep its own invariant, and the role use cases need
    /// it before they decide anything else -- because a code the platform does
    /// not have is a malformed request, while a code it does have but the caller
    /// may not hand out is a refusal, and the two must not be confused. Sharing
    /// the check keeps a single message and makes it impossible for the two
    /// callers to disagree about what the catalogue contains.
    /// </remarks>
    /// <exception cref="DomainValidationException">A code is not in the catalogue.</exception>
    public static void RequireRecognised(IEnumerable<PermissionCode> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (PermissionCode code in permissions)
        {
            if (!IsRecognised(code))
            {
                throw new DomainValidationException(
                    $"'{code}' is not a permission this platform recognises. Roles may only " +
                    "grant capabilities from the platform permission catalogue.");
            }
        }
    }

    /// <summary>
    /// The definition for a code, or <see langword="null"/> when it is not
    /// recognised.
    /// </summary>
    public static PermissionDefinition? Find(PermissionCode code) =>
        All.FirstOrDefault(definition => definition.Code == code);
}
