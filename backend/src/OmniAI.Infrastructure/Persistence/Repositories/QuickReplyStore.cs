using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class QuickReplyRow
{
    public Guid Id { get; init; }
    public TenantId TenantId { get; init; }
    public required string Language { get; init; }
    public required string Text { get; init; }
}

internal sealed class QuickReplyConfiguration : IEntityTypeConfiguration<QuickReplyRow>
{
    public void Configure(EntityTypeBuilder<QuickReplyRow> builder)
    {
        builder.ToTable("quick_replies");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.TenantId).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(row => row.Language).HasColumnName("language").HasMaxLength(2);
        builder.Property(row => row.Text).HasColumnName("text").HasMaxLength(500);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => new { row.TenantId, row.Language, row.Text }).IsUnique();
    }
}

internal sealed class QuickReplyStore(OmniAiDbContext context) : IQuickReplyStore
{
    public async Task<IReadOnlyList<QuickReply>> ListAsync(TenantId tenant, string language, CancellationToken token) =>
        await context.Set<QuickReplyRow>().Where(row => row.TenantId == tenant && row.Language == language)
            .OrderBy(row => row.Text).Select(row => new QuickReply(row.Id, row.Text)).ToListAsync(token).ConfigureAwait(false);

    public Task AddAsync(TenantId tenant, string language, string text, CancellationToken token) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO quick_replies (id, tenant_id, language, text) VALUES ({Guid.NewGuid()}, {tenant.Value}, {language}, {text})
            ON CONFLICT (tenant_id, language, text) DO NOTHING
            """, token);

    public Task DeleteAsync(TenantId tenant, string language, Guid id, CancellationToken token) =>
        context.Set<QuickReplyRow>().Where(row => row.TenantId == tenant && row.Language == language && row.Id == id)
            .ExecuteDeleteAsync(token);
}
