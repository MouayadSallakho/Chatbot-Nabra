using OmniAI.Domain.Authorization;

namespace OmniAI.Domain.Tests.Authorization;

/// <summary>
/// Phase 37.5b, row 26: every permission carries the stable key of its area, and
/// the keys never change.
/// </summary>
/// <remarks>
/// A snapshot on purpose. A client words each area by its key in the reader's
/// language; a key that moved would leave a heading untranslated, so changing one
/// must break this test and be decided, never happen by accident. A new permission
/// is added here with its key.
/// </remarks>
public sealed class PermissionGroupKeyTests
{
    private static readonly (string Code, string GroupKey)[] Snapshot =
    [
        ("workspace.view", "workspace"),
        ("workspace.manage", "workspace"),
        ("ai-agents.view", "ai-agents"),
        ("ai-agents.create", "ai-agents"),
        ("ai-agents.edit", "ai-agents"),
        ("ai-agents.lifecycle", "ai-agents"),
        ("ai-agents.preview", "ai-agents"),
        ("roles.view", "roles"),
        ("roles.manage", "roles"),
        ("members.view", "members"),
        ("members.invite", "members"),
        ("members.roles.manage", "members"),
        ("conversations.view", "conversations"),
        ("conversations.reply", "conversations"),
        ("conversations.handoff", "conversations"),
        ("conversations.assign", "conversations"),
        ("customers.view", "customers"),
        ("customers.manage", "customers"),
        ("website-chat.view", "channels"),
        ("website-chat.manage", "channels"),
        ("whatsapp.view", "channels"),
        ("whatsapp.manage", "channels"),
        ("knowledge.view", "knowledge"),
        ("knowledge.manage", "knowledge"),
        ("analytics.view", "analytics"),
        ("reports.view", "reports"),
        ("reports.generate", "reports"),
        ("reports.schedule", "reports"),
        ("billing.view", "billing"),
        ("billing.manage", "billing"),
    ];

    [Fact]
    public void Every_permission_has_the_group_key_it_was_published_with()
    {
        Assert.Equal(
            Snapshot.AsEnumerable(),
            PlatformPermissions.All.Select(permission => (permission.Code.Value, permission.GroupKey)));
    }

    [Fact]
    public void The_keys_are_the_eleven_the_plan_named()
    {
        Assert.Equal(
            ["workspace", "ai-agents", "roles", "members", "conversations", "customers", "channels", "knowledge", "analytics", "reports", "billing"],
            PlatformPermissions.All.Select(permission => permission.GroupKey).Distinct());
    }

    [Fact]
    public void One_area_has_one_key_and_one_key_one_area()
    {
        var pairs = PlatformPermissions.All.Select(permission => (permission.Group, permission.GroupKey)).Distinct().ToArray();

        Assert.Equal(pairs.Length, pairs.Select(pair => pair.Group).Distinct().Count());
        Assert.Equal(pairs.Length, pairs.Select(pair => pair.GroupKey).Distinct().Count());
    }

    [Fact]
    public void A_group_without_a_key_is_refused_rather_than_published()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlatformPermissions.GroupKeyFor("Something new"));
    }

    [Fact]
    public void A_key_is_lowercase_letters_and_hyphens()
    {
        Assert.All(PlatformPermissions.All, permission =>
            Assert.Matches("^[a-z]+(-[a-z]+)*$", permission.GroupKey));
    }
}
