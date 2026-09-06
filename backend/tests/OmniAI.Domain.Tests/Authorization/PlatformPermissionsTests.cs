using OmniAI.Domain.Authorization;

namespace OmniAI.Domain.Tests.Authorization;

/// <summary>
/// The catalogue: what this product says can be done.
/// </summary>
/// <remarks>
/// These are the tests that keep the catalogue honest as it grows. Most of them
/// are properties of the whole list rather than assertions about particular
/// entries, so they keep working -- and keep being worth something -- when the
/// next phase adds its capabilities.
/// </remarks>
public sealed class PlatformPermissionsTests
{
    [Fact]
    public void Every_definition_has_a_unique_code()
    {
        // Two entries sharing a code would make one of them unreachable, and
        // which one would depend on list order.
        IEnumerable<PermissionCode> codes = PlatformPermissions.All.Select(entry => entry.Code);

        Assert.Equal(PlatformPermissions.All.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Every_definition_carries_metadata_a_person_can_act_on()
    {
        // An administrator granting a capability should not have to guess what it
        // does from a code. A blank label or description would put them back to
        // guessing, and guessing about permissions is how too much gets granted.
        Assert.All(PlatformPermissions.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Group));
            Assert.False(string.IsNullOrWhiteSpace(entry.Label));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        });
    }

    [Fact]
    public void All_codes_matches_the_catalogue_exactly()
    {
        // AllCodes is what an Owner grants, so a divergence between it and the
        // catalogue would be an Owner quietly missing a capability -- the exact
        // failure this design exists to prevent.
        Assert.Equal(
            PlatformPermissions.All.Select(entry => entry.Code),
            PlatformPermissions.AllCodes);
    }

    [Fact]
    public void Every_catalogue_code_is_recognised()
    {
        Assert.All(PlatformPermissions.AllCodes, code =>
            Assert.True(PlatformPermissions.IsRecognised(code)));
    }

    [Fact]
    public void A_code_that_is_not_in_the_catalogue_is_not_recognised()
    {
        // Well-formed and plausible, and still refused. This is what stops a
        // caller inventing authority over a capability that does not exist.
        Assert.False(PlatformPermissions.IsRecognised(PermissionCode.Create("billing.manage")));
        Assert.False(PlatformPermissions.IsRecognised(PermissionCode.Create("ai-agents.delete")));
    }

    [Fact]
    public void The_catalogue_contains_no_capability_for_an_unimplemented_area()
    {
        // Listing a permission for a product area that does not exist would let
        // an administrator grant authority over nothing and then wonder why it
        // had no effect. Each arrives with the use cases it governs.
        string[] unimplemented =
            ["conversations", "knowledge", "channels", "billing", "integrations", "analytics"];

        Assert.All(PlatformPermissions.AllCodes, code =>
            Assert.DoesNotContain(
                unimplemented,
                area => code.Value.StartsWith(area + ".", StringComparison.Ordinal)));
    }

    [Fact]
    public void There_is_no_catch_all_administration_permission()
    {
        // A single permission covering everything would make every fine-grained
        // code beside it decorative: a role would be given the one that covers
        // the lot, and the distinctions would never be exercised. Full authority
        // is the Owner system role, which cannot be assembled from checkboxes.
        Assert.DoesNotContain(
            PlatformPermissions.AllCodes,
            code => code.Value is "tenant.admin" or "workspace.admin" or "admin.all");
    }

    [Fact]
    public void Each_current_capability_is_present_and_named_as_documented()
    {
        // The one place this suite pins exact codes. They are a published
        // contract -- they appear in stored grants, in API responses and in the
        // admin client -- so renaming one is a breaking change that should have
        // to be made deliberately here.
        Assert.Equal(
            [
                "workspace.view",
                "ai-agents.view",
                "ai-agents.create",
                "ai-agents.edit",
                "ai-agents.lifecycle",
                "ai-agents.preview",
                "roles.view",
                "roles.manage",
                "members.view",
                "members.invite",
                "members.roles.manage",
            ],
            PlatformPermissions.AllCodes.Select(code => code.Value));
    }

    [Fact]
    public void Definitions_are_grouped_contiguously_for_rendering()
    {
        // The role editor renders the catalogue in order under group headings. If
        // a group's entries were split apart, it would render the same heading
        // twice.
        List<string> groupsInOrder = [];

        foreach (PermissionDefinition entry in PlatformPermissions.All)
        {
            if (groupsInOrder.Count == 0 || groupsInOrder[^1] != entry.Group)
            {
                groupsInOrder.Add(entry.Group);
            }
        }

        Assert.Equal(groupsInOrder.Count, groupsInOrder.Distinct().Count());
    }

    [Fact]
    public void A_definition_can_be_found_by_its_code()
    {
        PermissionDefinition? found = PlatformPermissions.Find(PlatformPermissions.RolesManage);

        Assert.NotNull(found);
        Assert.Equal(PlatformPermissions.RolesGroup, found.Group);
    }

    [Fact]
    public void An_unrecognised_code_finds_nothing()
    {
        Assert.Null(PlatformPermissions.Find(PermissionCode.Create("billing.manage")));
    }
}
