namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public interface ICreatePaymentAttemptService
{
    Task<CreatePaymentAttemptResult> CreateAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default);
}