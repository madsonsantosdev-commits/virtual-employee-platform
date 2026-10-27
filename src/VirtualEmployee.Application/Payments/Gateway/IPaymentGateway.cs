namespace VirtualEmployee.Application.Payments.Gateway;

public interface IPaymentGateway
{
    Task<CreateGatewayPaymentResult> CreateAsync(
        CreateGatewayPaymentRequest request,
        CancellationToken cancellationToken = default);
}