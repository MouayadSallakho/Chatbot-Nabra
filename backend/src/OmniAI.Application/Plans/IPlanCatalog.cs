using OmniAI.Domain.Plans;

namespace OmniAI.Application.Plans;

/// <summary>
/// The plans this platform offers.
/// </summary>
/// <remarks>
/// <strong>Read-only, on purpose.</strong> There is no Add, no Update and no
/// Delete, because nothing in the running application creates or edits a plan:
/// plans arrive with the migration and change by deliberate deployment. A write
/// method here would eventually acquire a caller, and the first such caller would
/// be a workspace raising its own limits.
/// </remarks>
public interface IPlanCatalog
{
    /// <summary>The plan with this code, or null when no such plan exists.</summary>
    Task<Plan?> FindByCodeAsync(PlanCode code, CancellationToken cancellationToken = default);
}

/// <summary>
/// Which plan a newly created workspace is put on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Configuration, and it must be present.</strong> Read from
/// <c>Plans:DefaultPlanCode</c>. A deployment that omits it does not get a
/// sensible fallback — the danger of a fallback here is that it would be
/// "unlimited", and every production workspace created afterwards would silently
/// cost whatever it liked. Onboarding fails instead, loudly and immediately.
/// </para>
/// <para>
/// Development and test deployments name the development plan explicitly. That is
/// a decision somebody wrote down, not one the code made for them.
/// </para>
/// </remarks>
public sealed class DefaultPlanOptions
{
    /// <summary>The configuration section this is bound from.</summary>
    public const string Section = "Plans";

    /// <summary>The code of the plan new workspaces are assigned.</summary>
    public string DefaultPlanCode { get; set; } = string.Empty;
}

/// <summary>
/// This deployment cannot say which plan a new workspace should be on.
/// </summary>
/// <remarks>
/// Either <c>Plans:DefaultPlanCode</c> is missing, or it names a plan that is not
/// in the catalogue. Both are deployment faults, and both stop workspace creation
/// rather than inventing an entitlement.
/// </remarks>
public sealed class DefaultPlanUnavailableException(string reason)
    : Exception($"A new workspace cannot be created: {reason}");
