namespace OmniAI.Application.Tenants.GetTenantById;

/// <summary>
/// Request for one tenant's currently modelled information.
/// </summary>
/// <param name="TenantId">The raw identifier as it arrived from the caller.</param>
/// <remarks>
/// A <see cref="Guid"/> rather than a <c>TenantId</c>, because at this point the
/// value is still untrusted input. The handler converts it, so an empty or
/// malformed identifier is rejected by the Domain's own rule instead of being
/// silently accepted as <c>default</c>.
/// </remarks>
public sealed record GetTenantByIdQuery(Guid TenantId);
