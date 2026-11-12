using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// Tells a workspace's knowledge viewers that a document's indexing started or
/// settled (Phase 38.7a, BA-11).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Structural only.</strong> The payload is the document id and nothing
/// else: no title, no content, no attempt outcome. A client that hears it re-reads
/// the document through the authorised HTTP read, which is where the outcome lives
/// and where <c>knowledge.view</c> is checked again.
/// </para>
/// <para>
/// <strong>Best effort.</strong> Implementations should swallow their own
/// failures, and every caller announces through
/// <see cref="KnowledgeRealtimeAnnouncements.AnnounceQuietlyAsync"/>, which
/// guarantees it whatever is behind this interface: an announcement never changes
/// an attempt's outcome or its response.
/// </para>
/// </remarks>
public interface IKnowledgeRealtimeNotifier
{
    Task KnowledgeChangedAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Announcing a knowledge change without letting it break the change.</summary>
public static class KnowledgeRealtimeAnnouncements
{
    /// <summary>
    /// Announces that a document's indexing started or settled, and never throws.
    /// </summary>
    /// <remarks>
    /// A missing notifier announces nothing: the handler and the reconciler work
    /// without one, as they did before Phase 38.7a.
    /// </remarks>
    public static async Task AnnounceQuietlyAsync(
        this IKnowledgeRealtimeNotifier? realtime,
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        if (realtime is null)
        {
            return;
        }

        try
        {
            await realtime
                .KnowledgeChangedAsync(tenantId, documentId, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: the attempt is recorded either way; the transport logs its own failure.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}

/// <summary>
/// The server's own bound on one index attempt run for an HTTP request (Phase
/// 38.7a, BA-10).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The attempt no longer depends on the browser.</strong> The route runs
/// it under the host's shutdown token and this deadline instead of the request's
/// token, so closing or reloading the tab that pressed Index leaves the attempt to
/// finish, record its outcome and announce it.
/// </para>
/// <para>
/// <strong>The deadline sits under the stale-attempt reconciler's
/// threshold.</strong> It is the longest the provider work can legitimately take
/// (the same bound the reliability settings are validated against) plus
/// <see cref="Headroom"/> for reading and writing the index, and that threshold
/// must exceed the same bound by five minutes. So the reconciler never settles an
/// attempt that is still running.
/// </para>
/// </remarks>
public sealed class KnowledgeIndexingLimits
{
    /// <summary>Time for everything an attempt does besides the provider calls.</summary>
    public static readonly TimeSpan Headroom = TimeSpan.FromMinutes(2);

    /// <summary>Used when nothing configured one: ten minutes.</summary>
    public static readonly KnowledgeIndexingLimits Default = new(TimeSpan.FromMinutes(10));

    public KnowledgeIndexingLimits(TimeSpan attemptTimeout)
    {
        if (attemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptTimeout),
                attemptTimeout,
                "An index attempt needs a positive time limit.");
        }

        AttemptTimeout = attemptTimeout;
    }

    /// <summary>The longest one attempt may run before the server stops it.</summary>
    public TimeSpan AttemptTimeout { get; }

    /// <summary>The limits for provider work that can legitimately take this long.</summary>
    public static KnowledgeIndexingLimits For(TimeSpan longestProviderWork) =>
        new(longestProviderWork + Headroom);
}
