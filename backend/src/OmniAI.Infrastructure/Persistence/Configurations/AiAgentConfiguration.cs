using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

internal sealed class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("ai_agents");

        builder.HasKey(agent => agent.Id);

        builder.Property(agent => agent.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.AiAgentId)
            .ValueGeneratedNever();

        builder.Property(agent => agent.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Column lengths mirror the Domain limits. The application already
        // rejects longer values; the column makes the rule true of the data even
        // if something ever writes to this table without going through the model.
        builder.Property(agent => agent.Name)
            .HasColumnName("name")
            .HasConversion(PersistenceConverters.AiAgentName)
            .HasMaxLength(AiAgentName.MaxLength)
            .IsRequired();

        // A scalar column, not JSON, and not text: the Domain bound is a
        // character count, so character varying(20000) states the same rule the
        // Domain states. Interior line breaks and tabs are stored verbatim.
        builder.Property(agent => agent.Instructions)
            .HasColumnName("instructions")
            .HasConversion(PersistenceConverters.AgentInstructions)
            .HasMaxLength(AgentInstructions.MaxLength)
            .IsRequired();

        // Stored by name, not by ordinal. An integer column would make the stored
        // meaning depend on the declaration order of the enum, so reordering or
        // inserting a member would silently reinterpret every existing row.
        builder.Property(agent => agent.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // timestamptz stores an absolute instant. The Domain has already
        // normalised every value to UTC, so nothing here depends on the server's
        // or the client's time zone.
        builder.Property(agent => agent.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(agent => agent.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Restrict, not Cascade, and for the same reason as conversations: a
        // tenant that still owns agents must not be deletable by accident, and
        // certainly not silently. Cascade here would mean that whatever deletes a
        // tenant row also destroys every instruction set that tenant ever wrote
        // -- business content, not derived data -- as an invisible side effect.
        // Tenant deletion semantics do not exist yet, so the database refuses
        // until they are decided.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(agent => agent.TenantId)
            .HasConstraintName("fk_ai_agents_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Indexed on the tenant alone, which is what the actual access paths
        // justify and no more.
        //
        // The authorised lookup is by tenant AND id, and that one is already
        // optimal without this index: the primary key on id finds at most one
        // row, and the tenant is then checked against that row. A composite
        // (tenant_id, id) index would add write cost and win nothing.
        //
        // What does need it is the other side of the foreign key. PostgreSQL does
        // not index a referencing column automatically, so without this every
        // attempt to delete or re-key a tenant row would sequentially scan
        // ai_agents to enforce fk_ai_agents_tenant. It is also the index a
        // tenant-scoped listing will use when listing is authorised.
        //
        // Deliberately NOT unique on (tenant_id, name): no Domain or product rule
        // says an agent name is unique, the accepted Domain documentation records
        // that no uniqueness rule was invented, and a unique index here would
        // impose one through the back door -- rejecting a second "Sales
        // Assistant" for reasons no requirement asked for.
        builder.HasIndex(agent => agent.TenantId)
            .HasDatabaseName("ix_ai_agents_tenant");

        // Added in Phase 15, and not to prevent duplicates -- id is already the
        // primary key, so this pair can never repeat. PostgreSQL requires a unique
        // constraint on the columns a foreign key references, and both
        // web_chat_endpoints and web_chat_sessions reference exactly this pair so
        // that an endpoint's agent and its workspace cannot disagree. The same
        // construction tenant_roles already uses for role assignments.
        builder.HasAlternateKey(agent => new { agent.Id, agent.TenantId })
            .HasName("ak_ai_agents_id_tenant");

        // An agent is mutable -- name, instructions and status all change -- so
        // it carries the same optimistic concurrency token as the tenant and the
        // conversation. xmin is PostgreSQL's own per-row transaction id: it
        // already exists on every table, so nothing creates it, and EF reads it
        // and puts it in the UPDATE predicate. The Domain knows nothing about it,
        // and there is no Version property on the aggregate for EF's benefit.
        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
