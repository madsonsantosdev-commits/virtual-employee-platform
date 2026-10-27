using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Api.Endpoints;

public static class PaymentAttemptsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentAttemptsEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments")
            .WithTags("Payments");

        group.MapPost(
            "/{appointmentId:guid}/payment-attempts",
            async (
                Guid appointmentId,
                CreatePaymentAttemptRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                CreatePaymentAttemptHandler handler,
                CancellationToken cancellationToken) =>
            {
                PaymentMethod? method = request.PaymentMethod switch
                {
                    "PIX" => PaymentMethod.Pix,
                    "CREDIT_CARD" => PaymentMethod.CreditCard,
                    "DEBIT_CARD" => PaymentMethod.DebitCard,
                    _ => null
                };

                if (appointmentId == Guid.Empty || method is null)
                {
                    return Problem(
                        StatusCodes.Status400BadRequest,
                        "INVALID_REQUEST",
                        "Invalid payment attempt request.");
                }

                var result = await handler.HandleAsync(
                    new CreatePaymentAttemptCommand(
                        appointmentId,
                        method.Value,
                        idempotencyKey ?? string.Empty),
                    cancellationToken);

                if (result.IsSuccess && result.Response is { } response)
                {
                    return Results.Json(
                        response,
                        statusCode: StatusCodes.Status201Created);
                }

                return result.Error switch
                {
                    CreatePaymentAttemptError.InvalidRequest =>
                        Problem(
                            StatusCodes.Status400BadRequest,
                            "INVALID_REQUEST",
                            "Invalid payment attempt request."),

                    CreatePaymentAttemptError.AppointmentNotFound =>
                        Problem(
                            StatusCodes.Status404NotFound,
                            "RESOURCE_NOT_FOUND",
                            "Appointment was not found."),

                    CreatePaymentAttemptError.InvalidAppointmentState =>
                        Problem(
                            StatusCodes.Status409Conflict,
                            "PAYMENT_NOT_ALLOWED",
                            "The appointment does not allow payment."),

                    CreatePaymentAttemptError.ReservationExpired =>
                        Problem(
                            StatusCodes.Status409Conflict,
                            "RESERVATION_EXPIRED",
                            "The reservation has expired."),

                    CreatePaymentAttemptError.PaymentAlreadyConfirmed =>
                        Problem(
                            StatusCodes.Status422UnprocessableEntity,
                            "PAYMENT_ALREADY_CONFIRMED",
                            "The payment was already confirmed."),

                    CreatePaymentAttemptError.IdempotencyConflict =>
                        Problem(
                            StatusCodes.Status409Conflict,
                            "IDEMPOTENCY_CONFLICT",
                            "The key was already used with different content."),

                    CreatePaymentAttemptError.ConcurrencyConflict =>
                        Problem(
                            StatusCodes.Status409Conflict,
                            "CONCURRENCY_CONFLICT",
                            "The operation conflicts with another payment operation."),

                    CreatePaymentAttemptError.ProviderError =>
                        Problem(
                            StatusCodes.Status502BadGateway,
                            "PAYMENT_PROVIDER_ERROR",
                            "The payment provider result could not be completed."),

                    _ => Results.Problem(
                        title: "Payment attempt creation failed",
                        statusCode:
                            StatusCodes.Status500InternalServerError)
                };
            })
            .WithName("CreatePaymentAttempt")
            .WithSummary("Cria uma tentativa de pagamento integral")
            .WithDescription(
                "Aceita PIX, CREDIT_CARD ou DEBIT_CARD. " +
                "Exige Idempotency-Key. O valor é obtido do agendamento. " +
                "Criar a tentativa não confirma o pagamento nem o agendamento.")
            .Produces<CreatePaymentAttemptResponse>(
                StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static IResult Problem(
        int statusCode,
        string code,
        string detail)
    {
        return Results.Problem(
            title: "Payment attempt request failed",
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }

    public sealed record CreatePaymentAttemptRequest(
        string? PaymentMethod);
}