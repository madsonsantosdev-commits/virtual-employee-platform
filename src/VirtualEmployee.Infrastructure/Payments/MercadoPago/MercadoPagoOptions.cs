namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoOptions
{
    public const string SectionName = "Payments:MercadoPago";

    public List<MercadoPagoAccountOptions> Accounts { get; set; } = [];
}

public sealed class MercadoPagoAccountOptions
{
    public Guid TenantId { get; set; }

    public Guid BusinessId { get; set; }

    public string AccessToken { get; set; } = string.Empty;
}