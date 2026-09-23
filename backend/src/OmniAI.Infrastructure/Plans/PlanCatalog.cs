using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Plans;

/// <summary>
/// The plans this deployment has, read from the database.
/// </summary>
/// <remarks>
/// Plans arrive with the migration and change by deliberate deployment, so this
/// only ever reads. There is no write method, because the first caller of one
/// would be something letting a workspace choose its own limits.
/// </remarks>
internal sealed class PlanCatalog(OmniAiDbContext context) : IPlanCatalog
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<Plan?> FindByCodeAsync(
        PlanCode code,
        CancellationToken cancellationToken = default) =>
        await _context.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Code == code, cancellationToken)
            .ConfigureAwait(false);

    public async Task<Plan?> FindByIdAsync(
        PlanId planId,
        CancellationToken cancellationToken = default) =>
        await _context.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken)
            .ConfigureAwait(false);
}
