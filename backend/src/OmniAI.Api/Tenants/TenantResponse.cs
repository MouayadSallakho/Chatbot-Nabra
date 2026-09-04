using OmniAI.Application.Tenants;
using OmniAI.Domain.Tenants;

namespace OmniAI.Api.Tenants;

/// <summary>
/// The tenant representation returned by the tenant endpoints.
/// </summary>
/// <param name="Id">The tenant's identity.</param>
/// <param name="Name">The canonical display name.</param>
/// <param name="Slug">The canonical slug.</param>
/// <param name="Status">The operational state, as a stable name such as <c>Active</c>.</param>
/// <remarks>
/// The published HTTP contract, and the only thing a client sees. It is a
/// separate type from <c>TenantView</c> for one concrete reason: <c>Status</c> has
/// to appear on the wire as a name, and choosing that representation is a
/// transport decision. Keeping it here means the Application layer never has to
/// hold an opinion about JSON, and the wire format cannot drift because someone
/// changed a read model.
/// <para>
/// Four fields, all of them owned by the Domain today. No created or updated
/// timestamp, no plan, no subscription, no owner, no channel or AI configuration:
/// the aggregate has none of those, and publishing a field with nothing behind it
/// would be a promise this API cannot keep.
/// </para>
/// </remarks>
public sealed record TenantResponse(Guid Id, string Name, string Slug, string Status)
{
    /// <summary>Maps an Application read model onto the HTTP contract.</summary>
    internal static TenantResponse From(TenantView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new TenantResponse(view.Id, view.Name, view.Slug, Serialise(view.Status));
    }

    /// <summary>
    /// The wire name for a status.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to <c>ToString()</c> or a JSON enum
    /// converter. Both of those would make the published contract a side effect
    /// of an enum member's spelling or of a serialiser setting, so renaming a
    /// member or changing a global option would silently change what clients
    /// receive. Here the mapping is the contract, and adding a status without
    /// deciding its public name fails loudly instead of leaking an integer
    /// ordinal or an unexpected string.
    /// </remarks>
    private static string Serialise(TenantStatus status) => status switch
    {
        TenantStatus.Active => "Active",
        TenantStatus.Suspended => "Suspended",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "This tenant status has no published API representation."),
    };
}
