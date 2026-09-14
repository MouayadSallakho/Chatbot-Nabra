using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations;

/// <summary>Route a conversation to a member, or to nobody.</summary>
/// <param name="Member">
/// Who to route it to, or <see langword="null"/> to leave it unclaimed.
/// </param>
public sealed record AssignConversationCommand(
    Guid TenantId,
    Guid ConversationId,
    Guid? Member,
    ConcurrencyToken Expected);

/// <summary>The members a conversation may be routed to.</summary>
public sealed record ListAssigneesQuery(Guid TenantId);

/// <summary>
/// Deciding whose conversation this is.
/// </summary>
/// <remarks>
/// <strong>Assignment is routing, and confers nothing.</strong> Being assigned
/// does not let somebody reply, and not being assigned does not stop anybody
/// replying. It exists so two people do not answer the same customer at the same
/// time. That is why the delegation authority ceiling — which governs handing out
/// roles, where the danger is a member granting themselves more than they hold —
/// deliberately does not apply here: there is no permission to escalate.
/// <para>
/// <strong>Eligibility is checked, though, and for a product reason rather than a
/// security one.</strong> Routing a customer's conversation to a colleague who
/// cannot reply to it is a silent way to make that customer wait for ever. So the
/// target must be a member of this workspace and must currently hold
/// <c>conversations.reply</c>, computed from their roles at this moment rather
/// than read from anything stored.
/// </para>
/// <para>
/// <strong>The workspace boundary is enforced three times over.</strong> Here,
/// because the check is explicit; by the scoped lookup, which cannot even find a
/// conversation belonging to somebody else; and in the database, where the
/// assignment is a composite foreign key into this workspace's membership, so a
/// row naming an outsider is one PostgreSQL refuses to write.
/// </para>
/// <para>
/// It does not take the conversation lock. Assignment touches nothing a
/// generation touches — not the handling mode, not the transcript — so
/// serialising it behind a provider call would make an instant UI action wait on
/// somebody else's turn for no benefit. The version check is what keeps two
/// administrators from overwriting each other.
/// </para>
/// </remarks>
public sealed class ConversationAssignmentHandler(
    TenantAuthorizer authorizer,
    IConversationInboxStore store,
    IConversationRealtimeNotifier realtime,
    TimeProvider clock)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IConversationInboxStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IConversationRealtimeNotifier _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The conversation as it now stands, or <see langword="null"/> when the
    /// caller is not entitled or no such conversation belongs to this workspace.
    /// </returns>
    /// <exception cref="PermissionDeniedException">
    /// A member of the workspace without <c>conversations.assign</c>.
    /// </exception>
    /// <exception cref="ConversationAssigneeIneligibleException">
    /// The target is not a member here, or cannot reply.
    /// </exception>
    /// <exception cref="ConversationVersionMismatchException">
    /// The conversation changed after the caller read it.
    /// </exception>
    public async Task<Versioned<Conversation>?> AssignAsync(
        AssignConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        ConversationId conversationId = ConversationId.From(command.ConversationId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsAssign, cancellationToken)
            .ConfigureAwait(false);

        Versioned<Conversation>? found = await _store
            .FindForUpdateAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        if (found.Version != command.Expected)
        {
            throw new ConversationVersionMismatchException(command.Expected);
        }

        Conversation conversation = found.Value;
        DateTimeOffset now = _clock.GetUtcNow();
        bool changed;

        if (command.Member is null)
        {
            changed = conversation.Unassign(now);
        }
        else
        {
            UserId member = UserId.From(command.Member.Value);

            await RequireEligibleAsync(tenantId, member, cancellationToken).ConfigureAwait(false);

            changed = conversation.AssignTo(member, now);
        }

        if (!changed)
        {
            return found;
        }

        ConcurrencyToken version = await _store
            .SaveAsync(conversation, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        await _realtime
            .AnnounceQuietlyAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<Conversation>(conversation, version);
    }

    /// <returns>
    /// The members who may be routed a conversation, or <see langword="null"/>
    /// when the caller is not entitled.
    /// </returns>
    /// <remarks>
    /// Needs <c>conversations.assign</c> rather than <c>members.view</c>: it is a
    /// filtered list of colleagues published for one purpose, and somebody who
    /// cannot route a conversation has no reason to be given it.
    /// </remarks>
    public async Task<IReadOnlyList<ConversationMessageAuthor>?> ListAssigneesAsync(
        ListAssigneesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsAssign, cancellationToken)
            .ConfigureAwait(false);

        return await _store
            .ListEligibleAssigneesAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RequireEligibleAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken)
    {
        if (!await _store.IsMemberAsync(tenantId, member, cancellationToken).ConfigureAwait(false))
        {
            throw ConversationAssigneeIneligibleException.NotAMember();
        }

        IReadOnlyList<ConversationMessageAuthor> eligible = await _store
            .ListEligibleAssigneesAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!eligible.Any(candidate => candidate.UserId == member.Value))
        {
            throw ConversationAssigneeIneligibleException.CannotReply();
        }
    }
}
