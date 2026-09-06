using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// The platform's relational persistence context.
/// </summary>
/// <remarks>
/// Lives in Infrastructure and nowhere else. Domain does not know it exists, and
/// Application does not reference EF Core, so persistence can be changed without
/// either layer noticing.
/// <para>
/// Only aggregate roots get a <see cref="DbSet{TEntity}"/>. <c>Message</c> is
/// mapped but deliberately not exposed as a set: it belongs to a conversation,
/// and reaching it independently would route around the aggregate that enforces
/// its rules.
/// </para>
/// <para>
/// <strong>It derives from <c>IdentityUserContext</c>, not from
/// <c>IdentityDbContext</c>.</strong> That is the difference between four
/// Identity tables and seven: the user-only base omits roles, user-roles and
/// role-claims entirely. Phase 12 has no roles -- membership is the whole
/// authorization model -- so creating those three tables would have been
/// schema invented from a template, sitting empty and implying a permission
/// system that does not exist. The role tables arrive with the phase that
/// introduces roles, and their shape will be decided then.
/// </para>
/// <para>
/// <strong>One context over one database, rather than a separate identity
/// context.</strong> A membership row carries foreign keys to both a user and
/// a tenant, and creating a workspace has to write the tenant and the
/// membership in a single transaction. Two contexts could not hold either
/// guarantee without a distributed transaction or a hand-shared connection,
/// which would be considerably more machinery to protect a boundary that is
/// already enforced by which layer may name which type.
/// </para>
/// </remarks>
public sealed class OmniAiDbContext(DbContextOptions<OmniAiDbContext> options)
    : IdentityUserContext<OmniAiUser, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>
    /// Which people belong to which tenants: the platform's authorization
    /// boundary, as rows.
    /// </summary>
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<AiAgent> AiAgents => Set<AiAgent>();

    public override int SaveChanges()
    {
        List<EntityEntry<Message>> pending = PendingMessagesInAppendOrder();

        if (pending.Count > 0)
        {
            long[] allocated = [.. Database.SqlQuery<long>(
                $"SELECT nextval('message_ingestion_sequence')::bigint AS \"Value\" FROM generate_series(1, {pending.Count})")];

            StampIngestionOrder(pending, allocated);
        }

        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        List<EntityEntry<Message>> pending = PendingMessagesInAppendOrder();

        if (pending.Count > 0)
        {
            long[] allocated = [.. await Database.SqlQuery<long>(
                    $"SELECT nextval('message_ingestion_sequence')::bigint AS \"Value\" FROM generate_series(1, {pending.Count})")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)];

            StampIngestionOrder(pending, allocated);
        }

        return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the model: Identity's entities first, then this platform's.
    /// </summary>
    /// <param name="builder">
    /// The model builder. Named to match the base declaration rather than kept as
    /// <c>modelBuilder</c>: a parameter that disagrees with the member it
    /// overrides is a hazard for callers using named arguments, and the analyzer
    /// is right to say so.
    /// </param>
    /// <remarks>
    /// Order matters. <c>base.OnModelCreating</c> configures the Identity tables
    /// with their default names, and the configurations applied afterwards rename
    /// them. Reversing the two would silently restore <c>AspNetUsers</c>.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // The allocator for message ingestion order. See PersistenceSchema for
        // why a sequence rather than a per-conversation counter.
        builder.HasSequence<long>(PersistenceSchema.MessageIngestionSequenceName)
            .StartsAt(1)
            .IncrementsBy(1);

        builder.ApplyConfigurationsFromAssembly(typeof(OmniAiDbContext).Assembly);
    }

    /// <summary>
    /// The messages about to be inserted, in the order they were appended.
    /// </summary>
    /// <remarks>
    /// Two rejected designs sit behind this method, and both failed for the same
    /// underlying reason -- something other than the caller decided the order.
    /// <list type="number">
    /// <item>
    /// A database identity column, left to fill itself in. EF Core makes no
    /// promise about the order in which it inserts rows of one type inside a
    /// batch, so the numbers did not follow append order and reloaded
    /// transcripts came back scrambled.
    /// </item>
    /// <item>
    /// The message's index in the aggregate's collection. That produced the
    /// right order but only when the whole history had been loaded first, since
    /// otherwise the indices restarted at zero and collided with stored rows. A
    /// conversation may hold tens of thousands of messages, so making every
    /// append read all of them was not acceptable.
    /// </item>
    /// </list>
    /// What is left is the only thing that is genuinely both cheap and correct:
    /// the caller's append order supplies the <em>relative</em> order of the rows
    /// being inserted right now, and PostgreSQL supplies the <em>absolute</em>
    /// value. Loaded history contributes nothing at all -- not even as an offset
    /// -- which is why this method filters each conversation's collection down to
    /// the pending inserts instead of using positions within it.
    /// </remarks>
    private List<EntityEntry<Message>> PendingMessagesInAppendOrder()
    {
        List<EntityEntry<Message>> added =
        [
            .. ChangeTracker.Entries<Message>()
                .Where(entry => entry.State == EntityState.Added)
        ];

        // One message has no relative order to get wrong, and this is the normal
        // case: one delivery, one append, one save.
        if (added.Count <= 1)
        {
            return added;
        }

        Dictionary<Message, EntityEntry<Message>> byInstance =
            new(added.Count, ReferenceEqualityComparer.Instance);

        foreach (EntityEntry<Message> entry in added)
        {
            byInstance[entry.Entity] = entry;
        }

        List<EntityEntry<Message>> ordered = new(added.Count);

        foreach (EntityEntry<Conversation> conversationEntry in ChangeTracker.Entries<Conversation>())
        {
            foreach (Message candidate in conversationEntry.Entity.Messages)
            {
                if (byInstance.Remove(candidate, out EntityEntry<Message>? entry))
                {
                    ordered.Add(entry);
                }
            }
        }

        if (byInstance.Count == 0)
        {
            return ordered;
        }

        // A message whose conversation is not tracked cannot be placed relative
        // to the others, so it keeps the change tracker's order and goes last.
        // Nothing in this codebase produces that situation -- messages are only
        // reachable through their aggregate -- but silently dropping the row
        // would be worse than ordering it imperfectly.
        foreach (EntityEntry<Message> entry in added)
        {
            if (byInstance.ContainsKey(entry.Entity))
            {
                ordered.Add(entry);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Writes the allocated sequence values onto the pending rows, lowest value
    /// to earliest append.
    /// </summary>
    private static void StampIngestionOrder(
        List<EntityEntry<Message>> pending,
        long[] allocated)
    {
        if (allocated.Length != pending.Count)
        {
            throw new InvalidOperationException(
                $"Expected {pending.Count} ingestion order values from " +
                $"{PersistenceSchema.MessageIngestionSequenceName} but received " +
                $"{allocated.Length}. Refusing to write messages in an undefined order.");
        }

        // Sorted rather than trusted: the allocation query returns one row per
        // requested value, and nothing in the SQL standard promises the rows
        // arrive in the order the sequence produced them.
        Array.Sort(allocated);

        for (int index = 0; index < pending.Count; index++)
        {
            pending[index]
                .Property<long>(PersistenceSchema.MessageIngestionSequence)
                .CurrentValue = allocated[index];
        }
    }
}
