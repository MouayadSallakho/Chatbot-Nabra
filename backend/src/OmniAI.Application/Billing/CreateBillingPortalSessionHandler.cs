using OmniAI.Application.Authorization;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing;

/// <summary>Open the provider's own billing portal for a workspace.</summary>
public sealed record CreateBillingPortalSessionCommand(Guid TenantId);

/// <summary>Where to send the customer to manage their billing.</summary>
/// <remarks>
/// Short-lived, used once, and never stored: it is an authenticated way into
/// somebody's payment details, which is exactly the thing not to keep a copy of.
/// </remarks>
public sealed record BillingPortalSession(string Url);

/// <summary>This workspace has nothing for a portal to manage.</summary>
public sealed class BillingCustomerMissingException()
    : Exception("This workspace has no billing relationship to manage yet.")
{
    public static string Code => "billing.portal.nocustomer";
}

/// <summary>
/// Opens the provider's hosted billing portal.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Rebuilding this inside OmniAI would be the wrong trade.</strong>
/// Payment methods, invoices, receipts and cancellation are the provider's
/// speciality and their compliance surface. A card form here would move every one
/// of those obligations onto this platform in exchange for a slightly shorter
/// journey.
/// </para>
/// <para>
/// The return address comes from configuration, so the portal cannot be used to
/// bounce somebody to an arbitrary site.
/// </para>
/// </remarks>
public sealed class CreateBillingPortalSessionHandler(
    IBillingProvider provider,
    IWorkspaceBillingCustomerStore customers,
    BillingOptions options,
    TenantAuthorizer authorizer)
{
    private readonly IBillingProvider _provider =
        provider ?? throw new ArgumentNullException(nameof(provider));

    private readonly IWorkspaceBillingCustomerStore _customers =
        customers ?? throw new ArgumentNullException(nameof(customers));

    private readonly BillingOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <exception cref="TenantNotFoundException">Not a member, or no such workspace.</exception>
    /// <exception cref="PermissionDeniedException">A member without billing.manage.</exception>
    /// <exception cref="BillingCustomerMissingException">Nothing to manage yet.</exception>
    public async Task<BillingPortalSession> HandleAsync(
        CreateBillingPortalSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.BillingManage, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        if (!_options.IsConfigured)
        {
            throw new BillingNotConfiguredException("this deployment has no billing configuration");
        }

        WorkspaceBillingCustomer customer = await _customers
            .FindAsync(tenantId, _provider.Provider, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BillingCustomerMissingException();

        string url = await _provider
            .CreatePortalSessionAsync(
                customer.ProviderCustomerId,
                $"{_options.ReturnBaseUrl.TrimEnd('/')}/billing",
                cancellationToken)
            .ConfigureAwait(false);

        return new BillingPortalSession(url);
    }
}
