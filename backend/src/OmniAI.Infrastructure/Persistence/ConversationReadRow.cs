using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// One member's read marker on one conversation (Phase 37.4, migration M5).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A persistence row, not a Domain concept.</strong> It has no behaviour
/// beyond "only forward, never past the newest message", and that rule lives in
/// the single upsert that writes it, where two concurrent writers cannot
/// interleave. The list and the detail read it in the same query that reads the
/// conversation.
/// </para>
/// <para>
/// <strong>No backfill.</strong> A member with no row has never marked the
/// conversation read, so from the day this table appeared every conversation with
/// a message reads as unread until it is opened.
/// </para>
/// </remarks>
internal sealed class ConversationReadRow
{
    public ConversationId ConversationId { get; set; }

    public UserId UserId { get; set; }

    public TenantId TenantId { get; set; }

    /// <summary>The ingestion sequence of the newest message the member has seen.</summary>
    public long LastReadSequence { get; set; }

    /// <summary>When the marker last moved forward.</summary>
    public DateTimeOffset ReadAt { get; set; }
}
