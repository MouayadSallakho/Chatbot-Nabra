using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Plans;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// The platform's plans.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not tenant-scoped, and that is the point.</strong> There is no
/// <c>tenant_id</c> on this table: a plan belongs to the platform and is shared
/// by every workspace on it. A tenant column here would invite a tenant-facing
/// write path, and the first such path would let a workspace raise its own
/// limits.
/// </para>
/// <para>
/// <strong>Every limit is nullable, and null means unlimited.</strong> The
/// alternative — a sentinel like <c>-1</c> — would have to be understood
/// identically by the database, every query and every screen, and would be
/// summed or averaged by the first report that forgot. A check constraint keeps
/// finite limits at or above zero.
/// </para>
/// <para>
/// <strong>No price column.</strong> What a plan costs is Phase 25's subject; a
/// column here would be filled in by somebody long before there was anything to
/// charge it against.
/// </para>
/// </remarks>
internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.PlansTable, table =>
        {
            // The database's own guarantee, not FluentValidation's. A negative cap
            // is meaningless and must be impossible however the row arrives --
            // including by hand, from a migration, or from a future admin tool.
            foreach (string column in LimitColumns)
            {
                table.HasCheckConstraint(
                    $"ck_plans_{column}_not_negative",
                    $"\"{column}\" IS NULL OR \"{column}\" >= 0");
            }
        });

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.PlanId)
            .ValueGeneratedNever();

        builder.Property(plan => plan.Code)
            .HasColumnName("code")
            .HasConversion(PersistenceConverters.PlanCode)
            .HasMaxLength(PlanCode.MaxLength)
            .IsRequired();

        // The stable handle a deployment names in configuration, so two plans
        // cannot answer to the same name.
        builder.HasIndex(plan => plan.Code)
            .IsUnique()
            .HasDatabaseName("ux_plans_code");

        builder.Property(plan => plan.DisplayName)
            .HasColumnName("display_name")
            .HasConversion(PersistenceConverters.PlanDisplayName)
            .HasMaxLength(PlanDisplayName.MaxLength)
            .IsRequired();

        Limit(builder, "MaxMembersValue", "max_members");
        Limit(builder, "MaxAiAgentsValue", "max_ai_agents");
        Limit(builder, "MaxKnowledgeDocumentsValue", "max_knowledge_documents");
        Limit(builder, "MaxWebChatEndpointsValue", "max_web_chat_endpoints");
        Limit(builder, "MaxWhatsAppConnectionsValue", "max_whatsapp_connections");
        Limit(builder, "MaxMonthlyProviderOperationsValue", "max_monthly_provider_operations");

        builder.Property(plan => plan.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // No concurrency token. Nobody edits a plan: a change of commercial
        // meaning is a new plan, so there is no concurrent write to lose.
    }

    /// <summary>
    /// Maps one cap to a genuinely nullable column.
    /// </summary>
    /// <remarks>
    /// Mapped by name, against the aggregate's private <c>int?</c> properties. A
    /// converter from the non-nullable <c>PlanLimit</c> struct cannot produce a
    /// nullable column -- EF refuses to mark a non-nullable CLR type optional --
    /// and the only way past that without a sentinel is for the stored type to be
    /// the nullable one.
    /// </remarks>
    private static void Limit(EntityTypeBuilder<Plan> builder, string property, string column) =>
        builder.Property<int?>(property)
            .HasColumnName(column)
            .IsRequired(false);

    private static readonly string[] LimitColumns =
    [
        "max_members",
        "max_ai_agents",
        "max_knowledge_documents",
        "max_web_chat_endpoints",
        "max_whatsapp_connections",
        "max_monthly_provider_operations",
    ];
}

/// <summary>
/// Which plan each workspace is on.
/// </summary>
/// <remarks>
/// <strong>The tenant id is the primary key, so one workspace has exactly one
/// plan.</strong> Not a unique index over a table keyed by something else, and no
/// "is current" flag: with the tenant as the key there is no second row to
/// exclude, no ordering to get wrong and no window in which a workspace has two
/// plans or none.
/// </remarks>
internal sealed class WorkspacePlanAssignmentConfiguration
    : IEntityTypeConfiguration<WorkspacePlanAssignment>
{
    public void Configure(EntityTypeBuilder<WorkspacePlanAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.WorkspacePlanAssignmentsTable);

        builder.HasKey(assignment => assignment.TenantId);

        builder.Property(assignment => assignment.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.PlanId)
            .HasColumnName("plan_id")
            .HasConversion(PersistenceConverters.PlanId)
            .IsRequired();

        builder.Property(assignment => assignment.AssignedAt)
            .HasColumnName("assigned_at")
            .IsRequired();

        // Deleting a workspace takes its assignment with it: the row describes
        // that workspace and means nothing without it.
        builder.HasOne<Domain.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(assignment => assignment.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // A plan that workspaces are on cannot be deleted. Removing it would leave
        // those workspaces with no entitlements at all, which the service refuses
        // to interpret -- better to be unable to delete the plan.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(assignment => assignment.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
