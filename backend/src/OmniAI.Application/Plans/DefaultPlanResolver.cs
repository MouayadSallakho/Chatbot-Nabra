using OmniAI.Domain;
using OmniAI.Domain.Plans;

namespace OmniAI.Application.Plans;

/// <summary>
/// Resolves the plan a new workspace is created on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It fails rather than guesses.</strong> If <c>Plans:DefaultPlanCode</c>
/// is missing, or names a plan this deployment does not have, workspace creation
/// stops. The tempting alternative — fall back to something permissive — is the
/// one mistake that cannot be noticed: every production workspace created
/// afterwards would silently have no limits, and the first anybody would hear of
/// it is a provider bill.
/// </para>
/// <para>
/// Separate from <see cref="IPlanCatalog"/> because it answers a different
/// question. The catalogue says what exists; this says what this deployment was
/// configured to hand out.
/// </para>
/// </remarks>
public sealed class DefaultPlanResolver(IPlanCatalog catalog, DefaultPlanOptions options)
{
    private readonly IPlanCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    private readonly DefaultPlanOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    /// <exception cref="DefaultPlanUnavailableException">
    /// Nothing is configured, the configured code is not a usable plan code, or
    /// no plan carries it.
    /// </exception>
    public async Task<Plan> ResolveAsync(CancellationToken cancellationToken = default)
    {
        string configured = _options.DefaultPlanCode?.Trim() ?? string.Empty;

        if (configured.Length == 0)
        {
            throw new DefaultPlanUnavailableException(
                "this deployment has not configured Plans:DefaultPlanCode, and a workspace "
                + "must not be created without a plan.");
        }

        PlanCode code;

        try
        {
            code = PlanCode.Create(configured);
        }
        catch (DomainValidationException invalid)
        {
            throw new DefaultPlanUnavailableException(
                $"Plans:DefaultPlanCode is not a usable plan code ({invalid.Message})");
        }

        return await _catalog.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false)
            ?? throw new DefaultPlanUnavailableException(
                $"Plans:DefaultPlanCode names '{code}', which this deployment has no plan for.");
    }
}
