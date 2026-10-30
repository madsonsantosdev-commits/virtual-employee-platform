namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoAccountResolver
{
    private readonly MercadoPagoOptions _options;

    public MercadoPagoAccountResolver(MercadoPagoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    public string ResolveAccessToken(
        Guid tenantId,
        Guid businessId)
    {
        return FindAccount(tenantId, businessId).AccessToken;
    }

    public MercadoPagoAccountOptions ResolveForConfirmation(
        Guid tenantId,
        Guid businessId)
    {
        var account = FindAccount(tenantId, businessId);

        if (account.CollectorId <= 0)
        {
            throw new InvalidOperationException(
                "Mercado Pago collector ID is invalid.");
        }

        if (account.ApplicationId <= 0)
        {
            throw new InvalidOperationException(
                "Mercado Pago application ID is invalid.");
        }

        return new MercadoPagoAccountOptions
        {
            TenantId = account.TenantId,
            BusinessId = account.BusinessId,
            AccessToken = account.AccessToken,
            CollectorId = account.CollectorId,
            ApplicationId = account.ApplicationId
        };
    }

    private MercadoPagoAccountOptions FindAccount(
        Guid tenantId,
        Guid businessId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "TenantId must not be empty.",
                nameof(tenantId));
        }

        if (businessId == Guid.Empty)
        {
            throw new ArgumentException(
                "BusinessId must not be empty.",
                nameof(businessId));
        }

        var accounts = _options.Accounts
            ?? throw new InvalidOperationException(
                "Mercado Pago accounts are not configured.");

        var matches = accounts
            .Where(account =>
                account is not null &&
                account.TenantId == tenantId &&
                account.BusinessId == businessId)
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                "Mercado Pago account is not configured " +
                "for the requested tenant and business.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple Mercado Pago accounts are configured " +
                "for the requested tenant and business.");
        }

        var account = matches[0];

        if (string.IsNullOrWhiteSpace(account.AccessToken) ||
            account.AccessToken.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            throw new InvalidOperationException(
                "Mercado Pago access token is invalid.");
        }

        return account;
    }
}