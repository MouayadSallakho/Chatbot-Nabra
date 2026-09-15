using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Ai.Usage;

/// <summary>
/// Who is about to be charged for a provider call, and what for.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This travels as a parameter, deliberately.</strong> Every method that
/// can reach a paid provider takes one, so a new call site cannot compile without
/// saying whose money it is spending. The alternative — an ambient value read
/// from the current HTTP request or an <c>AsyncLocal</c> — works until the first
/// background worker, at which point calls are attributed to whatever happened to
/// be on the stack, and nobody notices until an invoice is questioned.
/// </para>
/// <para>
/// <strong>It carries no content.</strong> A workspace and a purpose from a
/// closed set: enough to group spend, and nothing that could describe what was
/// said.
/// </para>
/// </remarks>
public sealed record AiUsageContext
{
    private AiUsageContext(TenantId tenantId, AiUsagePurpose purpose)
    {
        TenantId = tenantId;
        Purpose = purpose;
    }

    /// <summary>The workspace this call is spent on behalf of.</summary>
    public TenantId TenantId { get; }

    /// <summary>Which flow asked for it.</summary>
    public AiUsagePurpose Purpose { get; }

    /// <exception cref="ArgumentException">No workspace was supplied.</exception>
    public static AiUsageContext For(TenantId tenantId, AiUsagePurpose purpose)
    {
        if (tenantId.IsEmpty)
        {
            throw new ArgumentException(
                "A provider call must be attributed to a workspace.", nameof(tenantId));
        }

        return new AiUsageContext(tenantId, purpose);
    }
}
