using VirtualEmployee.Application.Payments.ConfirmPayment;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Infrastructure.Payments;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;

namespace VirtualEmployee.Api.Configuration;

public static class MercadoPagoServiceCollectionExtensions
{
    public static IServiceCollection AddMercadoPagoGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(MercadoPagoOptions.SectionName)
            .Get<MercadoPagoOptions>()
            ?? throw new InvalidOperationException(
                "Mercado Pago configuration is missing.");

        if (options.Accounts is null || options.Accounts.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one Mercado Pago account must be configured.");
        }

        var resolver = new MercadoPagoAccountResolver(options);
        var webhookIds = new HashSet<Guid>();

        foreach (var account in options.Accounts)
        {
            if (account is null)
            {
                throw new InvalidOperationException(
                    "Mercado Pago account configuration is invalid.");
            }

            _ = resolver.ResolveAccessToken(account.TenantId, account.BusinessId);

            if (options.WebhooksEnabled)
            {
                _ = resolver.ResolveForConfirmation(
                    account.TenantId, account.BusinessId);

                if (account.WebhookId == Guid.Empty ||
                    !webhookIds.Add(account.WebhookId) ||
                    string.IsNullOrWhiteSpace(account.WebhookSecret))
                {
                    throw new InvalidOperationException(
                        "Each Mercado Pago account must have a unique nonempty " +
                        "webhook ID and a configured webhook secret.");
                }
            }
        }

        services.AddSingleton(options);
        services.AddSingleton(resolver);

        services
            .AddHttpClient<MercadoPagoOrdersClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });

        services
            .AddHttpClient<MercadoPagoPaymentsClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });

        services.AddScoped<IPaymentGateway, MercadoPagoGateway>();
        services.AddScoped<MercadoPagoPaymentVerificationService>();

        if (options.WebhooksEnabled)
        {
            services.AddScoped<MercadoPagoAttemptExpirationService>();
            services.AddScoped<MercadoPagoWebhookAuthenticator>();
            services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        }

        return services;
    }
}
