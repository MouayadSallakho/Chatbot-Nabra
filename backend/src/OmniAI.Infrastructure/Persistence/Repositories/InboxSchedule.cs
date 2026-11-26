using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class InboxSchedule
{
    public TenantId Tenant { get; init; }
    public bool Enabled { get; init; }
    // Each weekday has a start and end; -1 marks a closed day.
    public required int[] Minutes { get; init; }
    public AfterHoursTeam? Destination { get; init; }

    public RoutingHours Hours() => new(Enabled, Enumerable.Range(0, 7).Where(day => Minutes[2 * day] >= 0)
        .Select(day => new RoutingShift(day, Minutes[2 * day], Minutes[2 * day + 1])).ToArray(), Destination?.Role.Value);
}

internal sealed class AfterHoursTeam
{
    public TenantId Tenant { get; init; }
    public TenantRoleId Role { get; init; }
}

internal sealed class InboxScheduleConfiguration : IEntityTypeConfiguration<InboxSchedule>, IEntityTypeConfiguration<AfterHoursTeam>
{
    public void Configure(EntityTypeBuilder<InboxSchedule> builder)
    {
        builder.ToTable("inbox_schedules", t => t.HasCheckConstraint("ck_inbox_schedule_week", "cardinality(minutes) = 14"));
        builder.HasKey(s => s.Tenant);
        builder.Property(s => s.Tenant).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(s => s.Enabled).HasColumnName("enabled");
        builder.Property(s => s.Minutes).HasColumnName("minutes");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.Tenant).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Destination).WithOne().HasForeignKey<AfterHoursTeam>(r => r.Tenant).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<AfterHoursTeam> builder)
    {
        builder.ToTable("inbox_after_hours_teams");
        builder.HasKey(s => s.Tenant);
        builder.Property(s => s.Tenant).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(s => s.Role).HasColumnName("role_id").HasConversion(PersistenceConverters.TenantRoleId);
        builder.HasOne<TenantRole>().WithMany().HasForeignKey(r => new { r.Role, r.Tenant })
            .HasPrincipalKey(r => new { r.Id, r.TenantId }).OnDelete(DeleteBehavior.Cascade);
    }
}
