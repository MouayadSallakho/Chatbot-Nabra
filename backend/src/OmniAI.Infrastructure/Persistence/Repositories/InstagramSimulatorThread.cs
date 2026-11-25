using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class InstagramSimulatorThread
{
    public TenantId TenantId { get; init; }
    public AiAgentId AgentId { get; init; }
    public bool ExternalOwner { get; set; }
    public int OwnershipVersion { get; set; }
    public Guid Visitor { get; init; }
    public ConversationId ConversationId { get; set; }
}

internal sealed class InstagramSimulatorConfiguration : IEntityTypeConfiguration<InstagramSimulatorThread>
{
    public void Configure(EntityTypeBuilder<InstagramSimulatorThread> builder)
    {
        builder.ToTable("instagram_simulator_threads");
        builder.Property(row => row.ExternalOwner).HasColumnName("external_owner").HasDefaultValue(false);
        builder.Property(row => row.OwnershipVersion).HasColumnName("ownership_version").HasDefaultValue(0);
        builder.HasKey(row => new { row.TenantId, row.AgentId, row.Visitor });
        builder.Property(row => row.TenantId).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(row => row.AgentId).HasColumnName("agent_id").HasConversion(PersistenceConverters.AiAgentId);
        builder.Property(row => row.Visitor).HasColumnName("visitor");
        builder.Property(row => row.ConversationId).HasColumnName("conversation_id").HasConversion(PersistenceConverters.ConversationId);
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(row => row.AgentId);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(row => row.ConversationId);
        builder.HasIndex(row => row.ConversationId).IsUnique();
    }
}
