using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Authorization;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps which permissions each role grants.
/// </summary>
internal sealed class RolePermissionGrantConfiguration
    : IEntityTypeConfiguration<RolePermissionGrant>
{
    public void Configure(EntityTypeBuilder<RolePermissionGrant> builder)
    {
        builder.ToTable("role_permissions");

        // The pair is the key, and that is where "a role cannot grant the same
        // permission twice" is enforced -- structurally, rather than as an extra
        // unique constraint a later migration could drop without anything
        // obviously breaking. A surrogate key would make the duplicate row
        // possible and leave the application to notice.
        builder.HasKey(grant => new { grant.TenantRoleId, grant.Permission });

        builder.Property(grant => grant.TenantRoleId)
            .HasColumnName("tenant_role_id")
            .HasConversion(PersistenceConverters.TenantRoleId)
            .ValueGeneratedNever();

        // The code, stored as the string it is. Not an integer id: a row saying
        // 'ai-agents.edit' means the same thing in five years, in a backup and in
        // a log line, without a lookup table to consult -- and renumbering an
        // enum could never silently reinterpret an existing grant.
        //
        // No foreign key to a permissions table, because there is no permissions
        // table. The catalogue is compiled in, and a tenant-writable one would
        // let a workspace invent authority over capabilities that do not exist.
        // TenantRole checks every code against the catalogue before it becomes a
        // row, and drops any that a later release retires.
        builder.Property(grant => grant.Permission)
            .HasColumnName("permission")
            .HasConversion(PersistenceConverters.PermissionCode)
            .HasMaxLength(PermissionCode.MaxLength)
            .ValueGeneratedNever();

        // No index beyond the primary key. Every access path starts from a role
        // -- "what does this role grant" -- and tenant_role_id leads the key, so
        // that is already a range scan on it. The reverse question, "which roles
        // grant this permission", has no use case: nothing in the platform asks
        // it, and an index for it would cost every write to serve a query nobody
        // makes.

        // No xmin concurrency token. A grant is never updated -- the whole set is
        // replaced -- and the version that matters is the role's, which is what a
        // caller's If-Match names. A token here would imply an edit operation on
        // an individual grant, which is precisely the brittle surface this design
        // does not expose.
    }
}
