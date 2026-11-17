using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Application.Payments.RequestRefund;

public sealed record RequestRefundCommand(Guid PaymentId, RefundReason Reason, string IdempotencyKey);

public sealed record RequestRefundResponse(
    Guid RefundId,
    Guid PaymentId,
    Guid PaymentAttemptId,
    decimal Amount,
    string Status);

public enum RequestRefundError
{
    InvalidRequest,
    NotFound,
    NotAllowed,
    AlreadyCompleted,
    InProgress,
    IdempotencyConflict,
    ConcurrencyConflict
}

public sealed record RequestRefundResult(
    RequestRefundResponse? Response,
    RequestRefundError? Error)
{
    public static RequestRefundResult Accepted(RequestRefundResponse response) => new(response, null);
    public static RequestRefundResult Rejected(RequestRefundError error) => new(null, error);
}

public interface IRequestRefundService
{
    Task<RequestRefundResult> RequestAsync(RequestRefundCommand command, CancellationToken cancellationToken);
}

public sealed class RequestRefundHandler(IRequestRefundService service)
{
    public Task<RequestRefundResult> HandleAsync(
        RequestRefundCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentId == Guid.Empty ||
            !Enum.IsDefined(command.Reason) ||
            string.IsNullOrEmpty(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return Task.FromResult(RequestRefundResult.Rejected(RequestRefundError.InvalidRequest));
        }

        return service.RequestAsync(command, cancellationToken);
    }
}
