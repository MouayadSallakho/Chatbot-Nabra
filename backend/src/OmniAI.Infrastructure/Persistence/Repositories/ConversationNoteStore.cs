using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class StoredConversationNote
{
    public long Id { get; init; }
    public ConversationId ConversationId { get; init; }
    public Guid AuthorId { get; init; }
    public Guid RequestId { get; init; }
    public required string Text { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class ConversationNoteConfiguration : IEntityTypeConfiguration<StoredConversationNote>
{
    public void Configure(EntityTypeBuilder<StoredConversationNote> builder)
    {
        builder.ToTable("conversation_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(n => n.ConversationId).HasColumnName("conversation_id").HasConversion(PersistenceConverters.ConversationId);
        builder.Property(n => n.RequestId).HasColumnName("request_id");
        builder.Property(n => n.AuthorId).HasColumnName("author_id");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.Text).HasColumnName("text").HasMaxLength(ConversationNotesHandler.MaxLength);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(n => n.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.ConversationId, n.AuthorId, n.RequestId }).IsUnique();
        builder.HasIndex(n => new { n.ConversationId, n.Id });
    }
}

internal sealed class ConversationNoteStore(OmniAiDbContext db) : IConversationNoteStore
{
    private const int PageSize = 20;

    private IQueryable<StoredConversationNote> Notes(TenantId tenant, ConversationId conversation) =>
        db.Set<StoredConversationNote>().Where(n => n.ConversationId == conversation &&
            db.Conversations.Any(c => c.Id == n.ConversationId && c.TenantId == tenant));

    private IQueryable<ConversationNote> WithAuthor(IQueryable<StoredConversationNote> notes) =>
        notes.Select(n => new ConversationNote(n.Id, db.Users.Where(u => u.Id == n.AuthorId)
            .Select(u => u.DisplayName ?? u.Email).FirstOrDefault(), n.CreatedAt, n.Text));

    public async Task<ConversationNotePage?> ListAsync(TenantId tenant, ConversationId conversation, long? before, CancellationToken token)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == conversation && c.TenantId == tenant, token).ConfigureAwait(false))
        {
            return null;
        }
        var notes = Notes(tenant, conversation);
        if (before.HasValue)
        {
            notes = notes.Where(n => n.Id < before.Value);
        }
        var rows = await WithAuthor(notes.OrderByDescending(n => n.Id).Take(PageSize + 1)).ToListAsync(token).ConfigureAwait(false);
        var items = rows.Take(PageSize).ToArray();
        return new ConversationNotePage(items, rows.Count > PageSize ? items[^1].Id : null);
    }

    public async Task<ConversationNote?> AddAsync(TenantId tenant, ConversationId conversation, UserId author, Guid requestId,
        string text, DateTimeOffset now, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO conversation_notes (conversation_id, author_id, request_id, text, created_at)
            SELECT c.id, m.user_id, {requestId}, {text}, {now} FROM conversations c
            JOIN tenant_memberships m ON m.tenant_id = c.tenant_id AND m.user_id = {author.Value}
            WHERE c.id = {conversation.Value} AND c.tenant_id = {tenant.Value}
            ON CONFLICT (conversation_id, author_id, request_id) DO NOTHING
            """, token).ConfigureAwait(false);

        return await WithAuthor(Notes(tenant, conversation).Where(n => n.AuthorId == author.Value && n.RequestId == requestId))
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
    }
}
