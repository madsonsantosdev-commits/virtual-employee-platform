using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;

namespace VirtualEmployee.Api.Configuration;

public static class MercadoPagoServiceCollectionExtensions
{
    public static IServiceCollection AddMercadoPagoGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(MercadoPagoOptions.SectionName)
            .Get<MercadoPagoOptions>()
            ?? throw new InvalidOperationException(
                "Mercado Pago configuration is missing.");

        if (options.Accounts is null ||
            options.Accounts.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one Mercado Pago account must be configured.");
        }

        var resolver = new MercadoPagoAccountResolver(options);

        foreach (var account in options.Accounts)
        {
            if (account is null)
            {
                throw new InvalidOperationException(
                    "Mercado Pago account configuration is invalid.");
            }

            // Valida identificadores, duplicações e token,
            // sem registrar o valor da credencial.
            _ = resolver.ResolveAccessToken(
                account.TenantId,
                account.BusinessId);
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

        services.AddScoped<IPaymentGateway, MercadoPagoGateway>();

        return services;
    }
}