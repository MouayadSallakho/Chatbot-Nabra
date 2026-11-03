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

    /// <summary>
    /// Changing the workspace's own settings -- today its name, which visitors
    /// see on its public chat pages (Phase 37.5a, D-3); from Phase 37.6 also its
    /// time zone.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="WorkspaceView"/> and implying nothing, like every
    /// other code here. The Owner holds it with no data step, because the Owner's
    /// authority is <see cref="AllCodes"/>; a custom role is granted it like any
    /// other permission.
    /// </remarks>
    public static readonly PermissionCode WorkspaceManage = PermissionCode.Create("workspace.manage");

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
    /// It grants reading only, and is required for every other conversation
    /// capability: a person who may reply but may not read has no way to see
    /// what they are replying to. Exporting and deleting are still deliberately
    /// absent, because neither operation exists.
    /// </para>
    /// </remarks>
    public static readonly PermissionCode ConversationsView =
        PermissionCode.Create("conversations.view");

    /// <summary>Sending replies to customers as the workspace.</summary>
    /// <remarks>
    /// What this grants is writing to a customer in the workspace's name, which
    /// the customer will read as the business speaking. That is a different
    /// responsibility from being allowed to read a transcript, so it is a
    /// different permission — and holding it does not confer
    /// <see cref="ConversationsView"/>, because nothing in this platform implies
    /// anything else.
    /// <para>
    /// It is also what makes somebody eligible to have a conversation assigned to
    /// them. Assigning work to a person who cannot do it is a routing decision
    /// that can only end in the customer waiting.
    /// </para>
    /// </remarks>
    public static readonly PermissionCode ConversationsReply =
        PermissionCode.Create("conversations.reply");

    /// <summary>Taking a conversation over from the AI, and giving it back.</summary>
    /// <remarks>
    /// Separate from replying because it changes what happens to <em>future</em>
    /// customer messages rather than adding one of your own: taking over stops
    /// the AI answering, and returning hands it back. A workspace may reasonably
    /// want people who can answer a conversation somebody else has already
    /// escalated, without being able to escalate one themselves.
    /// </remarks>
    public static readonly PermissionCode ConversationsHandoff =
        PermissionCode.Create("conversations.handoff");

    /// <summary>Deciding which member a conversation is routed to.</summary>
    /// <remarks>
    /// Routing only. An assignment confers no permission on the member it names —
    /// it does not let them reply, and it does not stop anybody else replying —
    /// so this is not delegation and the authority ceiling that governs role
    /// assignment does not apply to it.
    /// </remarks>
    public static readonly PermissionCode ConversationsAssign =
        PermissionCode.Create("conversations.assign");

    /// <summary>Reading the workspace's customer directory.</summary>
    /// <remarks>
    /// <para>
    /// Who the workspace talks to, which channels each person reaches it on, and
    /// how much they have talked — not a word of what was said. A customer record
    /// is personal data in its own right: a list of everybody a business serves
    /// is worth having even without the transcripts, which is why reading it is
    /// its own capability rather than something a channel or conversation
    /// permission carries along.
    /// </para>
    /// <para>
    /// <strong>It does not grant transcript access.</strong> A customer's
    /// conversation history appears on their page only for somebody who also
    /// holds <see cref="ConversationsView"/>, so the directory cannot become a
    /// way around the permission that guards what customers actually said.
    /// </para>
    /// </remarks>
    public static readonly PermissionCode CustomersView =
        PermissionCode.Create("customers.view");

    /// <summary>Naming a customer, and combining two records into one.</summary>
    /// <remarks>
    /// <para>
    /// Two capabilities of very different weight sharing one permission, because
    /// both are the same kind of claim: this is who that person is. A label is
    /// easily corrected; a merge combines two people's channel identities and
    /// conversation history and cannot be undone in this phase, which is why the
    /// interface makes somebody confirm it in words.
    /// </para>
    /// <para>
    /// <strong>It is not authority over customers in general.</strong> There is
    /// deliberately no <c>customers.delete</c>: nothing in this platform removes
    /// a customer or their history, and a permission implying otherwise would be
    /// a promise the product does not keep.
    /// </para>
    /// </remarks>
    public static readonly PermissionCode CustomersManage =
        PermissionCode.Create("customers.manage");

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

    /// <summary>Read how the workspace's WhatsApp channel is configured.</summary>
    /// <remarks>
    /// Reading a connection tells somebody which number the workspace answers on
    /// and which agent answers there. It never tells them the access token: no
    /// read path in this platform returns one, whatever permission the caller
    /// holds.
    /// </remarks>
    public static readonly PermissionCode WhatsAppView = PermissionCode.Create("whatsapp.view");

    /// <summary>Configure the WhatsApp channel, and replace its credential.</summary>
    /// <remarks>
    /// This is what connects a workspace's AI to a phone number the public can
    /// message, and what disconnects it again — so it is separate from reading,
    /// for the same reason publishing a chat link is separate from seeing that
    /// one exists. It also covers replacing the access token, which is a
    /// write-only operation: the value goes in and is never shown again.
    /// </remarks>
    public static readonly PermissionCode WhatsAppManage =
        PermissionCode.Create("whatsapp.manage");

    /// <summary>Read the workspace's knowledge and try retrieval out.</summary>
    public static readonly PermissionCode KnowledgeView = PermissionCode.Create("knowledge.view");

    /// <summary>Write knowledge, index it, switch it on and off, and decide which
    /// agents may draw on it.</summary>
    public static readonly PermissionCode KnowledgeManage =
        PermissionCode.Create("knowledge.manage");

    /// <summary>
    /// Read the workspace's own numbers.
    /// </summary>
    /// <remarks>
    /// There is deliberately no matching manage permission. Analytics has nothing
    /// to configure: it is a read over operational records that other capabilities
    /// already govern, so a second code would grant authority over nothing.
    /// </remarks>
    public static readonly PermissionCode AnalyticsView = PermissionCode.Create("analytics.view");

    /// <summary>Read the workspace's subscription and what it could buy.</summary>
    public static readonly PermissionCode BillingView = PermissionCode.Create("billing.view");

    /// <summary>
    /// Start a subscription and open the provider's billing portal.
    /// </summary>
    /// <remarks>
    /// <strong>Commercial authority, and nothing more.</strong> It buys, it opens
    /// the portal, and it widens nothing else: somebody holding it is still bound
    /// by the delegation ceiling, still cannot grant themselves permissions, and
    /// still meets every Phase 24 entitlement limit like anybody else. Spending
    /// money is not administration.
    /// </remarks>
    public static readonly PermissionCode BillingManage = PermissionCode.Create("billing.manage");

    /// <summary>See the report catalogue, generated reports, and schedules.</summary>
    /// <remarks>
    /// Downloading a report additionally needs whatever that report's data needs
    /// -- <c>customers.view</c> for customer activity, <c>analytics.view</c> for
    /// the rest -- so this permission never becomes a way around those.
    /// </remarks>
    public static readonly PermissionCode ReportsView = PermissionCode.Create("reports.view");

    /// <summary>Generate and export a report.</summary>
    /// <remarks>
    /// Never implied by <see cref="ReportsView"/>: reading a list of reports is not
    /// deciding that the workspace's data should be written to a file.
    /// </remarks>
    public static readonly PermissionCode ReportsGenerate = PermissionCode.Create("reports.generate");

    /// <summary>Create, change and switch off report schedules.</summary>
    /// <remarks>
    /// A schedule generates under its owner's authority, and that authority is
    /// checked again at every occurrence.
    /// </remarks>
    public static readonly PermissionCode ReportsSchedule = PermissionCode.Create("reports.schedule");

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

    /// <summary>
    /// Who the workspace talks to. Deliberately not part of Conversations.
    /// </summary>
    /// <remarks>
    /// A separate group because the two answer different questions and are
    /// granted to different people: a directory is an administrative view of the
    /// customer base, and a transcript is what somebody said in confidence.
    /// </remarks>
    public const string CustomersGroup = "Customers";

    /// <summary>The channel administration group's display name.</summary>
    public const string ChannelsGroup = "Channels";

    public const string KnowledgeGroup = "Knowledge";

    /// <summary>The analytics group's display name.</summary>
    public const string AnalyticsGroup = "Analytics";

    /// <summary>Subscription and payment. Deliberately not part of Analytics.</summary>
    public const string BillingGroup = "Billing";

    /// <summary>Exports and scheduled reports. Separate from Analytics.</summary>
    public const string ReportsGroup = "Reports";

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
            WorkspaceManage,
            WorkspaceGroup,
            "Manage workspace",
            "Change the workspace's settings, such as its name, which visitors see on "
            + "its public chat."),
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
            ConversationsReply,
            ConversationsGroup,
            "Reply to conversations",
            "Write to customers as this workspace. Members who hold it are also "
            + "the members a conversation can be assigned to."),
        new(
            ConversationsHandoff,
            ConversationsGroup,
            "Take conversations over from the AI",
            "Stop the AI answering a conversation so a person can, and hand it "
            + "back again afterwards."),
        new(
            ConversationsAssign,
            ConversationsGroup,
            "Assign conversations",
            "Decide which team member a conversation is routed to. It gives that "
            + "member no permission they did not already have."),
        new(
            CustomersView,
            CustomersGroup,
            "View customers",
            "See who this workspace talks to, which channels they use and how "
            + "much they have talked. It does not grant access to what they said."),
        new(
            CustomersManage,
            CustomersGroup,
            "Manage customers",
            "Name a customer, and combine two records that turn out to be the "
            + "same person. Combining cannot be undone."),
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
            WhatsAppView,
            ChannelsGroup,
            "View WhatsApp",
            "See how the workspace's WhatsApp channel is set up and whether it is "
            + "in service. It never reveals the access token."),
        new(
            WhatsAppManage,
            ChannelsGroup,
            "Manage WhatsApp",
            "Connect a WhatsApp number to an AI agent, replace its access token, "
            + "and take the channel in and out of service."),
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
        new(
            AnalyticsView,
            AnalyticsGroup,
            "View analytics",
            "See how many conversations the workspace handled, how its channels "
            + "and AI agents are being used, and how much activity happened each "
            + "day. Counts only -- never the text of a conversation."),
        new(
            ReportsView,
            ReportsGroup,
            "View reports",
            "See which reports exist and download generated ones. Each download also "
            + "needs the permission that report's data needs."),
        new(
            ReportsGenerate,
            ReportsGroup,
            "Generate reports",
            "Export the workspace's numbers as CSV, Excel or PDF files. Counts and "
            + "safe labels only -- never the text of a conversation."),
        new(
            ReportsSchedule,
            ReportsGroup,
            "Schedule reports",
            "Create weekly and monthly reports that are generated automatically "
            + "under your own permissions."),
        new(
            BillingView,
            BillingGroup,
            "View billing",
            "See the workspace's subscription, what it is paying for and what "
            + "else it could buy. Never a card number: payment details live with "
            + "the payment provider and never reach this platform."),
        new(
            BillingManage,
            BillingGroup,
            "Manage billing",
            "Start a subscription and open the payment provider's own billing "
            + "portal, where payment methods, invoices and cancellation are "
            + "handled. This is authority to spend the workspace's money."),
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
