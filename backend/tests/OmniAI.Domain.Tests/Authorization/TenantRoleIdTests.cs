using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Authorization;

public sealed class TenantRoleIdTests
{
    [Fact]
    public void A_new_identity_is_a_uuid_version_7()
    {
        // Version 7 keeps the time-ordered insert locality the whole schema
        // relies on for its indexes. Version 4 would scatter inserts across the
        // b-tree, and a sequential integer would leak how many roles exist.
        Guid value = TenantRoleId.New().Value;

        Assert.Equal(7, value.ToByteArray(bigEndian: true)[6] >> 4);
    }

    [Fact]
    public void New_identities_are_distinct()
    {
        Assert.NotEqual(TenantRoleId.New(), TenantRoleId.New());
    }

    [Fact]
    public void An_empty_guid_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => TenantRoleId.From(Guid.Empty));
    }

    [Fact]
    public void An_existing_value_round_trips()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(value, TenantRoleId.From(value).Value);
    }

    [Fact]
    public void The_default_value_is_reported_as_empty()
    {
        Assert.True(default(TenantRoleId).IsEmpty);
        Assert.False(TenantRoleId.New().IsEmpty);
    }

    [Fact]
    public void A_role_id_is_not_interchangeable_with_another_identifier()
    {
        // The substitution this type exists to prevent is not hypothetical: a
        // role id sits beside a tenant id in a route, both are GUIDs, and a
        // transposition would compile if either were a bare Guid. The pair is
        // what scopes a role to its workspace, so a swap is exactly the shape a
        // cross-tenant modification would take.
        Assert.NotEqual(typeof(TenantRoleId), typeof(TenantId));

        Assert.DoesNotContain(
            typeof(TenantRoleId).GetMethods(),
            method => method.Name is "op_Implicit" or "op_Explicit");
    }

    [Fact]
    public void The_string_form_is_the_canonical_guid_format()
    {
        Guid value = Guid.CreateVersion7();

        Assert.Equal(value.ToString("D"), TenantRoleId.From(value).ToString());
    }
}
