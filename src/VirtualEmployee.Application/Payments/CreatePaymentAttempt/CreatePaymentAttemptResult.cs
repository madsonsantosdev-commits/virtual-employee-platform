namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public sealed record CreatePaymentAttemptResult
{
    public CreatePaymentAttemptError Error { get; }
    public CreatePaymentAttemptResponse? Response { get; }

    public bool IsSuccess =>
        Error == CreatePaymentAttemptError.None;

    private CreatePaymentAttemptResult(
        CreatePaymentAttemptError error,
        CreatePaymentAttemptResponse? response)
    {
        Error = error;
        Response = response;
    }

    public static CreatePaymentAttemptResult Success(
        CreatePaymentAttemptResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new CreatePaymentAttemptResult(
            CreatePaymentAttemptError.None,
            response);
    }

    public static CreatePaymentAttemptResult Failure(
        CreatePaymentAttemptError error)
    {
        if (error == CreatePaymentAttemptError.None ||
            !Enum.IsDefined(error))
        {
            throw new ArgumentOutOfRangeException(
                nameof(error),
                "Failure requires a valid error.");
        }

        return new CreatePaymentAttemptResult(error, null);
    }
}