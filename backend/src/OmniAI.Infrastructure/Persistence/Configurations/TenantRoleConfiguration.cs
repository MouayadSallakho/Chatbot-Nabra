using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the roles a workspace can hand out.
/// </summary>
internal sealed class TenantRoleConfiguration : IEntityTypeConfiguration<TenantRole>
{
    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("tenant_roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.TenantRoleId)
            .ValueGeneratedNever();

        builder.Property(role => role.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // The display form, as the workspace typed it. Case is preserved because
        // it is what a person reads in the list.
        builder.Property(role => role.Name)
            .HasColumnName("name")
            .HasConversion(PersistenceConverters.TenantRoleName)
            .HasMaxLength(TenantRoleName.MaxLength)
            .IsRequired();

        // The form uniqueness is decided on.
        //
        // A shadow property rather than part of the name's own mapping, because a
        // value converter fills one column and this name needs two. The role
        // repository sets it from TenantRoleName.Normalised on every write.
        //
        // The alternative considered was a PostgreSQL generated column,
        // "GENERATED ALWAYS AS (upper(name)) STORED", which would need no
        // application code at all. It was rejected because upper() is
        // collation-dependent: for a non-ASCII role name the database's idea of
        // the normalised form and ToUpperInvariant's could differ, and this
        // platform would then have two definitions of when two role names are the
        // same. One definition, in the Domain, is worth one assignment in the
        // repository -- and the integration suite asserts a case-variant name is
        // actually refused by the index rather than trusting that it would be.
        builder.Property<string>(PersistenceSchema.NormalisedRoleName)
            .HasColumnName("normalized_name")
            .HasMaxLength(TenantRoleName.MaxLength)
            .IsRequired();

        // Stored by name, not by ordinal, like every other enum in this schema. An
        // integer column would make the stored meaning depend on the declaration
        // order of TenantRoleKind, so inserting a member would silently reclassify
        // every existing row -- and here that would mean reclassifying protected
        // system roles as editable ones.
        builder.Property(role => role.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(role => role.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(role => role.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Restrict, not Cascade, for the same reason every other tenant-owned
        // table uses it: deleting a tenant row must not silently destroy the
        // authorisation model of the workspace as an invisible side effect.
        // Tenant deletion semantics do not exist yet, so the database refuses
        // until they are decided.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(role => role.TenantId)
            .HasConstraintName("fk_tenant_roles_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The grants this role carries.
        //
        // A collection navigation over a private field, so the aggregate keeps
        // control of what it grants: there is no public setter, no Add and no
        // Remove that bypasses TenantRole.ReplacePermissions and therefore no way
        // to store a grant the catalogue does not recognise.
        //
        // Cascade here, unlike every other relationship in this schema, and it is
        // the one place where cascade is right: a grant has no meaning without the
        // role that carries it, it is not business content anybody would want to
        // recover, and the alternative would be orphan rows the application could
        // never see. It also makes "replace the whole set" one statement rather
        // than a delete the repository has to remember.
        builder.HasMany(role => role.Grants)
            .WithOne()
            .HasForeignKey(grant => grant.TenantRoleId)
            .HasConstraintName("fk_role_permissions_role")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(role => role.Grants)
            .HasField("_grants")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Role names are unique within a workspace, ignoring capitalisation --
        // and NOT globally. Two businesses may each have a "Support agent" role,
        // and a name one of them takes must not affect the other, so tenant_id
        // leads the index.
        //
        // The index is the authority, not the handler's pre-check: two
        // administrators creating the same name at the same instant both pass the
        // check, and only this decides which one wins.
        builder.HasIndex(
                nameof(TenantRole.TenantId),
                PersistenceSchema.NormalisedRoleName)
            .HasDatabaseName("ux_tenant_roles_tenant_name")
            .IsUnique();

        // At most one Owner per workspace, held by the database rather than by
        // the application alone.
        //
        // A partial unique index: it constrains only the rows where kind =
        // 'Owner', so a workspace may have any number of custom roles while a
        // second Owner is refused outright. Nothing in the application creates
        // one -- TenantRole.CreateOwner is reachable only from provisioning and
        // the backfill -- but "nothing currently calls it twice" is a property of
        // today's code, and this is a property of the data.
        //
        // The application guarantees at least one: provisioning writes it in the
        // same transaction as the tenant. A database cannot express "at least
        // one" as an index, and expressing it as a trigger would put business
        // rules somewhere nobody reviewing this codebase would look.
        builder.HasIndex(role => role.TenantId)
            .HasDatabaseName("ux_tenant_roles_owner")
            .IsUnique()
            .HasFilter("kind = 'Owner'");

        // The composite key that makes cross-tenant role assignment
        // unrepresentable.
        //
        // On its own this looks redundant -- id is already the primary key, so
        // (id, tenant_id) can never be duplicated. It is not here to prevent
        // duplicates: PostgreSQL requires a unique constraint on the columns a
        // foreign key references, and tenant_role_assignments references exactly
        // this pair. That is what forces an assignment's role and its membership
        // to name the same tenant, because both of its foreign keys read the same
        // tenant_id column. See TenantRoleAssignmentConfiguration.
        builder.HasAlternateKey(role => new { role.Id, role.TenantId })
            .HasName("ak_tenant_roles_id_tenant");

        // A role is mutable -- its name and its permission set both change -- so
        // it carries the same optimistic concurrency token as the tenant, the
        // conversation and the AI agent. xmin is PostgreSQL's own per-row
        // transaction id: it already exists on every table, so nothing creates it,
        // and EF reads it and puts it in the UPDATE predicate. The Domain knows
        // nothing about it, and there is no Version property on the aggregate for
        // EF's benefit.
        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
