using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Report requests, and the claim that says which worker is generating one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two unique keys carry the idempotency.</strong> A manual request is
/// unique per (workspace, requester, client request id), so a double-click is one
/// row. A scheduled occurrence is unique per (schedule, period start), so a crash
/// or two instances cannot queue the same week twice. Both are partial, because
/// each applies to only one kind of row.
/// </para>
/// <para>
/// <strong>The claim is persistence, not domain.</strong> The token and the
/// claim instant are shadow columns: they exist so the database can decide which
/// worker owns an execution, and nothing outside the store needs to read them.
/// </para>
/// </remarks>
internal sealed class ReportExecutionConfiguration : IEntityTypeConfiguration<ReportExecution>
{
    public const string ClaimToken = "ClaimToken";
    public const string ClaimedAt = "ClaimedAt";

    public void Configure(EntityTypeBuilder<ReportExecution> builder)
    {
        builder.ToTable(PersistenceSchema.ReportExecutionsTable, table =>
        {
            table.HasCheckConstraint("ck_report_executions_period", "period_to > period_from");
            table.HasCheckConstraint("ck_report_executions_attempts", "attempts >= 0");
        });

        builder.HasKey(execution => execution.Id);

        builder.Property(execution => execution.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ReportExecutionId)
            .ValueGeneratedNever();

        builder.Property(execution => execution.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(execution => execution.Type)
            .HasColumnName("report_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(execution => execution.Format)
            .HasColumnName("format")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(execution => execution.PeriodFrom)
            .HasColumnName("period_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(execution => execution.PeriodTo)
            .HasColumnName("period_to")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Phase 37.6 (M8): the zone the period's days were computed in. A model
        // default, so executions made before it read UTC, which is what they were.
        builder.Property(execution => execution.TimeZone)
            .HasColumnName("time_zone")
            .HasConversion(PersistenceConverters.WorkspaceTimeZone)
            .HasMaxLength(WorkspaceTimeZone.MaxLength)
            .HasDefaultValue(WorkspaceTimeZone.Utc)
            .IsRequired();

        // Phase 37.7 (M9): the language the file is written in. A model default, so
        // executions made before it read English, which is what they were.
        builder.Property(execution => execution.Language)
            .HasColumnName("language")
            .HasConversion(PersistenceConverters.UserLanguage)
            .HasMaxLength(UserLanguages.MaxCodeLength)
            .HasDefaultValue(UserLanguage.English)
            .IsRequired();

        builder.Property(execution => execution.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(execution => execution.RequestedBy)
            .HasColumnName("requested_by")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(execution => execution.RequestedAt)
            .HasColumnName("requested_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(execution => execution.ClientRequestId).HasColumnName("client_request_id");

        builder.Property(execution => execution.ScheduleId)
            .HasColumnName("schedule_id")
            .HasConversion(PersistenceConverters.NullableReportScheduleId);

        builder.Property(execution => execution.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(execution => execution.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(execution => execution.FailedAt)
            .HasColumnName("failed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(execution => execution.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(40);

        builder.Property(execution => execution.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(execution => execution.ExpiredAt)
            .HasColumnName("expired_at")
            .HasColumnType("timestamp with time zone");

        builder.Property<Guid?>(ClaimToken).HasColumnName("claim_token");

        builder.Property<DateTimeOffset?>(ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        builder.Ignore(execution => execution.Period);
        builder.Ignore(execution => execution.IsTerminal);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(execution => execution.TenantId)
            .HasConstraintName("fk_report_executions_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Composite, so an occurrence can only ever point at a schedule of its
        // own workspace.
        builder.HasOne<ReportSchedule>()
            .WithMany()
            .HasForeignKey(execution => new { execution.TenantId, execution.ScheduleId })
            .HasPrincipalKey(schedule => new { schedule.TenantId, schedule.Id })
            .HasConstraintName("fk_report_executions_schedule")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(execution => new { execution.TenantId, execution.RequestedAt, execution.Id })
            .HasDatabaseName("ix_report_executions_tenant_requested")
            .IsDescending(false, true, true);

        builder.HasIndex(execution => new { execution.TenantId, execution.RequestedBy, execution.ClientRequestId })
            .HasDatabaseName("ux_report_executions_client_request")
            .IsUnique()
            .HasFilter("client_request_id IS NOT NULL");

        builder.HasIndex(execution => new { execution.ScheduleId, execution.PeriodFrom })
            .HasDatabaseName("ux_report_executions_occurrence")
            .IsUnique()
            .HasFilter("schedule_id IS NOT NULL");

        // The worker's own query: the small set of rows still to be done.
        builder.HasIndex(execution => new { execution.Status, execution.RequestedAt })
            .HasDatabaseName("ix_report_executions_claimable")
            .HasFilter("status IN ('Pending', 'Running')");
    }
}

/// <summary>Metadata for one generated file.</summary>
internal sealed class ReportArtifactConfiguration : IEntityTypeConfiguration<ReportArtifact>
{
    public void Configure(EntityTypeBuilder<ReportArtifact> builder)
    {
        builder.ToTable(PersistenceSchema.ReportArtifactsTable, table =>
        {
            table.HasCheckConstraint("ck_report_artifacts_length", "byte_length > 0");
            table.HasCheckConstraint("ck_report_artifacts_expiry", "expires_at > created_at");
        });

        builder.HasKey(artifact => artifact.Id);

        builder.Property(artifact => artifact.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ReportArtifactId)
            .ValueGeneratedNever();

        builder.Property(artifact => artifact.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(artifact => artifact.ExecutionId)
            .HasColumnName("execution_id")
            .HasConversion(PersistenceConverters.ReportExecutionId)
            .IsRequired();

        builder.Property(artifact => artifact.Format)
            .HasColumnName("format")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(artifact => artifact.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(artifact => artifact.ByteLength)
            .HasColumnName("byte_length")
            .IsRequired();

        builder.Property(artifact => artifact.Sha256)
            .HasColumnName("sha256")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        // Opaque and server-generated. Never serialised by any endpoint.
        builder.Property(artifact => artifact.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(artifact => artifact.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(artifact => artifact.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<ReportExecution>()
            .WithMany()
            .HasForeignKey(artifact => new { artifact.TenantId, artifact.ExecutionId })
            .HasPrincipalKey(execution => new { execution.TenantId, execution.Id })
            .HasConstraintName("fk_report_artifacts_execution")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(artifact => artifact.ExecutionId)
            .HasDatabaseName("ux_report_artifacts_execution")
            .IsUnique();

        builder.HasIndex(artifact => artifact.StorageKey)
            .HasDatabaseName("ux_report_artifacts_storage_key")
            .IsUnique();

        builder.HasIndex(artifact => artifact.ExpiresAt)
            .HasDatabaseName("ix_report_artifacts_expires");
    }
}

/// <summary>Recurring reports.</summary>
internal sealed class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable(PersistenceSchema.ReportSchedulesTable);

        builder.HasKey(schedule => schedule.Id);

        builder.Property(schedule => schedule.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ReportScheduleId)
            .ValueGeneratedNever();

        builder.Property(schedule => schedule.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Workspace-authored and untrusted. Bounded here as well as in the Domain.
        builder.Property(schedule => schedule.Name)
            .HasColumnName("name")
            .HasMaxLength(ReportSchedule.MaxNameLength)
            .IsRequired();

        builder.Property(schedule => schedule.Type)
            .HasColumnName("report_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(schedule => schedule.Format)
            .HasColumnName("format")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(schedule => schedule.Frequency)
            .HasColumnName("frequency")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(schedule => schedule.Enabled).HasColumnName("enabled").IsRequired();

        builder.Property(schedule => schedule.NextRunAt)
            .HasColumnName("next_run_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(schedule => schedule.OwnerUserId)
            .HasColumnName("owner_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(schedule => schedule.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(schedule => schedule.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(schedule => schedule.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(schedule => schedule.DisabledReason)
            .HasColumnName("disabled_reason")
            .HasMaxLength(40);

        // Phase 37.7 (M10): the language every occurrence is written in. A model
        // default, so schedules made before it read English. A new schedule takes
        // its creator's language: an application rule, not a column default.
        builder.Property(schedule => schedule.Language)
            .HasColumnName("language")
            .HasConversion(PersistenceConverters.UserLanguage)
            .HasMaxLength(UserLanguages.MaxCodeLength)
            .HasDefaultValue(UserLanguage.English)
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(schedule => schedule.TenantId)
            .HasConstraintName("fk_report_schedules_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(schedule => new { schedule.TenantId, schedule.Name })
            .HasDatabaseName("ix_report_schedules_tenant_name");

        // The due-schedule query: enabled rows by the next boundary.
        builder.HasIndex(schedule => schedule.NextRunAt)
            .HasDatabaseName("ix_report_schedules_due")
            .HasFilter("enabled");

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .IsRowVersion();
    }
}
