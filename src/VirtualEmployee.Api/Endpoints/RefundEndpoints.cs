using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Payments.RequestRefund;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Api.Endpoints;

public static class RefundEndpoints
{
    public static void MapRefundEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/payments/{paymentId:guid}/refunds", async (
            Guid paymentId,
            RefundRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            RequestRefundHandler handler,
            CancellationToken cancellationToken) =>
        {
            var reason = request.Reason switch
            {
                "CUSTOMER_CANCELLATION" => RefundReason.CustomerCancellation,
                "BUSINESS_CANCELLATION" => RefundReason.BusinessCancellation,
                "LATE_PAYMENT" => RefundReason.LatePayment,
                _ => (RefundReason?)null
            };

            if (reason is null)
            {
                return Problem(400, "INVALID_REQUEST");
            }

            var result = await handler.HandleAsync(
                new RequestRefundCommand(paymentId, reason.Value, idempotencyKey ?? string.Empty),
                cancellationToken);

            return result.Response is { } response
                ? Results.Accepted(value: response)
                : result.Error switch
                {
                    RequestRefundError.InvalidRequest => Problem(400, "INVALID_REQUEST"),
                    RequestRefundError.NotFound => Problem(404, "RESOURCE_NOT_FOUND"),
                    RequestRefundError.NotAllowed => Problem(422, "REFUND_NOT_ALLOWED"),
                    RequestRefundError.AlreadyCompleted => Problem(422, "REFUND_ALREADY_COMPLETED"),
                    RequestRefundError.InProgress => Problem(409, "REFUND_IN_PROGRESS"),
                    RequestRefundError.IdempotencyConflict => Problem(409, "IDEMPOTENCY_CONFLICT"),
                    RequestRefundError.ConcurrencyConflict => Problem(409, "CONCURRENCY_CONFLICT"),
                    _ => Problem(500, "INTERNAL_ERROR")
                };
        })
        .WithName("RequestRefund")
        .WithTags("Refunds")
        .Produces<RequestRefundResponse>(StatusCodes.Status202Accepted);
    }

    private static IResult Problem(int status, string code) =>
        Results.Problem(
            statusCode: status,
            title: "Refund request failed",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public sealed record RefundRequest(string? Reason);
}
