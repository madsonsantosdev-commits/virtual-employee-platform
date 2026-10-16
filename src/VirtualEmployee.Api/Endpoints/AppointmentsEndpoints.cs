using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.Appointments.GetAppointmentById;
namespace VirtualEmployee.Api.Endpoints;

public static class AppointmentsEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentsEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments")
            .WithTags("Appointments");

        group.MapPost("", async (
            CreateAppointmentRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CreateAppointmentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new CreateAppointmentCommand(
                    request.CustomerId,
                    request.LocationId,
                    request.ProfessionalId,
                    request.ServiceIds ?? Array.Empty<Guid>(),
                    request.StartsAt,
                    idempotencyKey ?? string.Empty),
                cancellationToken);

            if (result.IsSuccess && result.Response is { } response)
            {
                return Results.Created(
                    $"/api/v1/appointments/{response.Id}",
                    response);
            }

            return result.Error switch
            {
                CreateAppointmentError.InvalidRequest =>
                    Results.BadRequest(),

                CreateAppointmentError.CustomerNotFound or
                CreateAppointmentError.LocationNotFound or
                CreateAppointmentError.BusinessNotFound or
                CreateAppointmentError.ProfessionalNotFound or
                CreateAppointmentError.ServiceNotFound or
                CreateAppointmentError.ProfessionalLocationNotFound =>
                    Results.NotFound(),

                CreateAppointmentError.SlotUnavailable =>
                    Conflict(
                        "SLOT_UNAVAILABLE",
                        "The requested slot is unavailable."),

                CreateAppointmentError.IdempotencyConflict =>
                    Conflict(
                        "IDEMPOTENCY_CONFLICT",
                        "The key was already used with different content."),

                CreateAppointmentError.InvalidTimezone =>
                    Results.Problem(
                        title: "Appointment configuration error",
                        statusCode:
                            StatusCodes.Status500InternalServerError),

                CreateAppointmentError.InactiveLocation or
                CreateAppointmentError.InactiveBusiness or
                CreateAppointmentError.InactiveProfessional or
                CreateAppointmentError.CustomerFromAnotherBusiness or
                CreateAppointmentError.ProfessionalFromAnotherBusiness or
                CreateAppointmentError.InactiveProfessionalLocation or
                CreateAppointmentError.InvalidServices or
                CreateAppointmentError.ServicesNotOffered or
                CreateAppointmentError.ProfessionalNotEligible or
                CreateAppointmentError.InvalidTotals =>
                    Conflict(
                        "APPOINTMENT_CONFLICT",
                        "Resources are incompatible with the appointment."),

                _ => Results.Problem(
                    title: "Appointment creation failed",
                    statusCode:
                        StatusCodes.Status500InternalServerError)
            };
        });

        group.MapGet("/{appointmentId:guid}", async (
            Guid appointmentId,
            GetAppointmentByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (appointmentId == Guid.Empty)
            {
                return Results.BadRequest();
            }

            var response = await handler.HandleAsync(
                appointmentId,
                cancellationToken);

            return response is null
                ? Results.NotFound()
                : Results.Ok(response);
        });

        return app;
    }

    private static IResult Conflict(string code, string detail)
    {
        return Results.Problem(
            title: "Appointment conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }

    public sealed record CreateAppointmentRequest(
        Guid CustomerId,
        Guid LocationId,
        Guid ProfessionalId,
        IReadOnlyList<Guid>? ServiceIds,
        DateTimeOffset StartsAt);
}