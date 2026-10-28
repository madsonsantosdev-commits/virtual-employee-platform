namespace VirtualEmployee.Application.Payments.ConfirmPayment;

public interface IPaymentConfirmationService
{
    Task<ConfirmPaymentResult> ConfirmAsync(
        ConfirmPaymentCommand command,
        CancellationToken cancellationToken = default);
}