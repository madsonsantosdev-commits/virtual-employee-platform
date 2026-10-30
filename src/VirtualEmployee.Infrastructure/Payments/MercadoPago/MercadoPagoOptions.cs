namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "Payments:MercadoPago";

    public bool WebhooksEnabled { get; set; }

    public List<MercadoPagoAccountOptions> Accounts { get; set; } = [];
}

public sealed class MercadoPagoAccountOptions
{
    public Guid TenantId { get; set; }

    public Guid BusinessId { get; set; }

    public string AccessToken { get; set; } = string.Empty;

    public long CollectorId { get; set; }

    public long ApplicationId { get; set; }

    public Guid WebhookId { get; set; }

    public string WebhookSecret { get; set; } = string.Empty;
}