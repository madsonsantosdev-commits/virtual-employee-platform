using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Application.Payments.Gateway;

namespace VirtualEmployee.Infrastructure.Payments;

public sealed record PaymentAttemptPreparationResult
{
    public CreatePaymentAttemptError Error { get; }
    public CreateGatewayPaymentRequest? Request { get; }

    public bool IsSuccess =>
        Error == CreatePaymentAttemptError.None;

    private PaymentAttemptPreparationResult(
        CreatePaymentAttemptError error,
        CreateGatewayPaymentRequest? request)
    {
        Error = error;
        Request = request;
    }

    public static PaymentAttemptPreparationResult Success(
        CreateGatewayPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PaymentAttemptPreparationResult(
            CreatePaymentAttemptError.None,
            request);
    }

    public static PaymentAttemptPreparationResult Failure(
        CreatePaymentAttemptError error)
    {
        if (error == CreatePaymentAttemptError.None ||
            !Enum.IsDefined(error))
        {
            throw new ArgumentOutOfRangeException(nameof(error));
        }

        return new PaymentAttemptPreparationResult(error, null);
    }
}