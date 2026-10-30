namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoWebhookAuthenticator
{
    private readonly MercadoPagoOptions _options;
    private readonly MercadoPagoAccountResolver _accountResolver;

    public MercadoPagoWebhookAuthenticator(
        MercadoPagoOptions options,
        MercadoPagoAccountResolver accountResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(accountResolver);

        _options = options;
        _accountResolver = accountResolver;
    }

    public MercadoPagoAccountOptions? Authenticate(
        Guid webhookId,
        string? signature,
        string? requestId,
        string? dataId)
    {
        if (!_options.WebhooksEnabled ||
            webhookId == Guid.Empty)
        {
            return null;
        }

        var accounts = _options.Accounts
            ?? throw new InvalidOperationException(
                "Mercado Pago accounts are not configured.");

        var matches = accounts
            .Where(account =>
                account is not null &&
                account.WebhookId == webhookId)
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                "Multiple Mercado Pago accounts use the same webhook ID.");
        }

        var account = matches[0];

        if (string.IsNullOrWhiteSpace(account.WebhookSecret))
        {
            throw new InvalidOperationException(
                "Mercado Pago webhook secret is not configured.");
        }

        if (!MercadoPagoWebhookSignatureValidator.Validate(
                account.WebhookSecret,
                signature,
                requestId,
                dataId))
        {
            return null;
        }

        return _accountResolver.ResolveForConfirmation(
            account.TenantId,
            account.BusinessId);
    }
}