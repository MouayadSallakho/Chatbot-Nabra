using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// One element of the ASP.NET Core Data Protection key ring.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> Session cookies and antiforgery tokens are
/// protected with the Data Protection key ring. Left to the default, the ring
/// lives on each process's local disk: a redeployed container starts with a new
/// ring, so every session and every antiforgery token issued before is suddenly
/// unreadable, and two instances behind one load balancer cannot read each
/// other's cookies at all. Keeping the ring in PostgreSQL gives every instance
/// and every restart the same keys.
/// </para>
/// <para>
/// <strong>Encrypted at rest when a master key is configured.</strong> The
/// <c>xml</c> column then holds AES-256-GCM ciphertext under
/// <c>OMNIAI_SECRET_MASTER_KEY</c> — the same key, and the same protector, as
/// channel credentials. Without one it holds the framework's plain XML, exactly
/// as the default file-system ring would.
/// </para>
/// </remarks>
public sealed class DataProtectionKeyRow
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public string Xml { get; set; } = string.Empty;
}

internal sealed class DataProtectionKeyRowConfiguration : IEntityTypeConfiguration<DataProtectionKeyRow>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyRow> builder)
    {
        builder.ToTable(PersistenceSchema.DataProtectionKeysTable);

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(row => row.FriendlyName).HasColumnName("friendly_name").HasMaxLength(200);
        builder.Property(row => row.Xml).HasColumnName("xml").IsRequired();
    }
}
