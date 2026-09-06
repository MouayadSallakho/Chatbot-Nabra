using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps which members hold which roles.
/// </summary>
/// <remarks>
/// <strong>This table is where cross-tenant authorisation is made
/// impossible.</strong> Everything else about it is ordinary; the two composite
/// foreign keys below are the reason it carries a tenant column at all.
/// </remarks>
internal sealed class TenantRoleAssignmentConfiguration
    : IEntityTypeConfiguration<TenantRoleAssignment>
{
    public void Configure(EntityTypeBuilder<TenantRoleAssignment> builder)
    {
        builder.ToTable("tenant_role_assignments");

        // All three columns are the key. A person either holds a role in a
        // workspace or does not, so a second row for the same triple has no
        // meaning -- and with a surrogate key it would be possible. This makes
        // the uniqueness structural rather than an extra constraint.
        builder.HasKey(assignment => new
        {
            assignment.UserId,
            assignment.TenantId,
            assignment.TenantRoleId,
        });

        builder.Property(assignment => assignment.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.UserId)
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.TenantRoleId)
            .HasColumnName("tenant_role_id")
            .HasConversion(PersistenceConverters.TenantRoleId)
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The first half of the cross-tenant guarantee: the assignment must
        // correspond to a real membership, matched on BOTH columns.
        //
        // A key on user_id alone would prove the person exists. This proves they
        // belong to the workspace this assignment names -- so an assignment
        // cannot give authority to somebody who is not a member of the tenant it
        // is in, and revoking a membership cannot leave a role behind, because
        // the membership row cannot be removed while this one references it.
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(
                assignment => new { assignment.UserId, assignment.TenantId })
            .HasConstraintName("fk_tenant_role_assignments_membership")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The second half, and the one that closes it: the role must belong to
        // the same workspace.
        //
        // It references tenant_roles(id, tenant_id) -- the alternate key declared
        // in TenantRoleConfiguration -- rather than tenant_roles(id). That is the
        // whole trick: both foreign keys read this row's own tenant_id column, so
        // PostgreSQL will accept the row only when the membership and the role
        // agree about which workspace they are in. A row pairing a membership in
        // tenant A with a role from tenant B is not rejected by application code;
        // it cannot be written at all.
        //
        // Restrict, so a role that somebody still holds cannot be deleted out
        // from under them. Role deletion is not implemented in this phase, so
        // nothing meets this refusal today -- but when it arrives, the database
        // will force the question "what happens to the members holding it" to be
        // answered rather than answered by accident.
        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(
                assignment => new { assignment.TenantRoleId, assignment.TenantId })
            .HasPrincipalKey(role => new { role.Id, role.TenantId })
            .HasConstraintName("fk_tenant_role_assignments_role")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // "What may this person do in this workspace" is the platform's most
        // frequent authorisation question, and user_id then tenant_id lead the
        // primary key, so it is a range scan on the key with no extra index.
        //
        // This one exists for the other direction. PostgreSQL does not index a
        // foreign key's referencing columns automatically, so without it every
        // check of the role reference -- and any future "who holds this role" --
        // would scan the table.
        builder.HasIndex(assignment => new
        {
            assignment.TenantRoleId,
            assignment.TenantId,
        })
            .HasDatabaseName("ix_tenant_role_assignments_role");

        // No xmin concurrency token. An assignment is inserted and would be
        // deleted; nothing updates one, so there is no lost update to prevent and
        // a token would imply an edit operation that does not exist.
    }
}
