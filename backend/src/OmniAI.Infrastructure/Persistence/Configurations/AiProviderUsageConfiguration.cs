using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Ai;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provider spend, as a table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is absent from this mapping is the point.</strong> There is no
/// column for a prompt, a completion, instructions, grounding, a knowledge
/// excerpt, a customer message, a vector, an API key, or a provider request or
/// response body — and none may be added. Counts, identifiers, rates and
/// instants only: enough to reconcile an invoice, never enough to reconstruct a
/// conversation.
/// </para>
/// <para>
/// <strong>Token counts are nullable in the database as well as in the
/// Domain.</strong> A <c>NOT NULL DEFAULT 0</c> would turn "the provider did not
/// say" into "the provider said nothing was used", which is the single most
/// misleading thing this table could do.
/// </para>
/// <para>
/// <strong>Money is <c>numeric</c>.</strong> Never <c>double precision</c>: a
/// cost that cannot be represented exactly is a cost nobody can reconcile.
/// </para>
/// <para>
/// No concurrency token. Rows are written once and settled once, and the
/// aggregate already refuses a second settle — there is no lost-update to
/// protect against, and an <c>xmin</c> check would only add a failure mode to a
/// write that must not fail.
/// </para>
/// </remarks>
internal sealed class AiProviderUsageConfiguration : IEntityTypeConfiguration<AiProviderUsage>
{
    public void Configure(EntityTypeBuilder<AiProviderUsage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.AiProviderUsagesTable);

        builder.HasKey(usage => usage.Id);

        builder.Property(usage => usage.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.AiProviderUsageId)
            .ValueGeneratedNever();

        builder.Property(usage => usage.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Stored by name, like every other enum here: a row is readable in psql
        // without a lookup table, and renumbering an enum cannot silently
        // reinterpret history.
        builder.Property(usage => usage.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(usage => usage.OperationType)
            .HasColumnName("operation_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(usage => usage.Purpose)
            .HasColumnName("purpose")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(usage => usage.RequestedModel)
            .HasColumnName("requested_model")
            .HasConversion(PersistenceConverters.ModelName)
            .HasMaxLength(ModelName.MaxLength)
            .IsRequired();

        builder.Property(usage => usage.ResolvedModel)
            .HasColumnName("resolved_model")
            .HasConversion(PersistenceConverters.NullableModelName)
            .HasMaxLength(ModelName.MaxLength);

        builder.Property(usage => usage.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(usage => usage.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(usage => usage.CompletedAt)
            .HasColumnName("completed_at");

        // The provider's own counts, each independently absent.
        builder.OwnsOne(usage => usage.Usage, tokens =>
        {
            tokens.Property(value => value.InputTokens).HasColumnName("input_tokens");
            tokens.Property(value => value.CachedInputTokens).HasColumnName("cached_input_tokens");
            tokens.Property(value => value.OutputTokens).HasColumnName("output_tokens");
            tokens.Property(value => value.ReasoningTokens).HasColumnName("reasoning_tokens");
            tokens.Property(value => value.TotalTokens).HasColumnName("total_tokens");
        });

        builder.Navigation(usage => usage.Usage).IsRequired();

        // Eighteen digits and six decimal places: a single call's cost is a
        // fraction of a cent, and a quarter's total is not. Both fit.
        builder.Property(usage => usage.EstimatedCost)
            .HasColumnName("estimated_cost")
            .HasPrecision(18, 6);

        builder.Property(usage => usage.Currency)
            .HasColumnName("currency")
            .HasConversion(PersistenceConverters.NullableCurrencyCode)
            .HasMaxLength(3);

        // The rate snapshot. Copied onto the row so a later price change cannot
        // rewrite what this call was rated with.
        builder.Property(usage => usage.InputRatePerMillion)
            .HasColumnName("input_rate_per_million")
            .HasPrecision(18, 6);

        builder.Property(usage => usage.CachedInputRatePerMillion)
            .HasColumnName("cached_input_rate_per_million")
            .HasPrecision(18, 6);

        builder.Property(usage => usage.OutputRatePerMillion)
            .HasColumnName("output_rate_per_million")
            .HasPrecision(18, 6);

        builder.Property(usage => usage.RatesEffectiveFrom)
            .HasColumnName("rates_effective_from");

        builder.Property(usage => usage.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // The one access path this table has: one workspace's spend over a
        // window, ordered by when the money was committed. Every analytics query
        // and every reconciliation starts here.
        builder.HasIndex(usage => new { usage.TenantId, usage.StartedAt })
            .HasDatabaseName("ix_ai_provider_usages_tenant_started");

        // Restrict rather than cascade. Provider spend is financial evidence:
        // deleting a workspace must not silently erase the record of money this
        // platform actually paid out.
        builder.HasOne<Domain.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(usage => usage.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
