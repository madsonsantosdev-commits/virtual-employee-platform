namespace VirtualEmployee.Application.Payments.Gateway;

public sealed record GatewayPaymentReference(
    string Provider,
    string ProviderPaymentId,
    string? CheckoutUrl,
    DateTimeOffset ExpiresAt);