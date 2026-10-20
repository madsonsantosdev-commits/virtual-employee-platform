using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.Appointments.GetAppointmentById;
using VirtualEmployee.Application.Appointments.GetAppointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using VirtualEmployee.Domain.Scheduling;
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

        group.MapGet(
            "",
            async (
                Guid? locationId,
                Guid? professionalId,
                Guid? customerId,
                string? status,
                DateTimeOffset? from,
                DateTimeOffset? to,
                int? page,
                int? pageSize,
                GetAppointmentsHandler handler,
                CancellationToken cancellationToken) =>
            {
                AppointmentStatus? parsedStatus = status switch
                {
                    null => null,
                    "PENDING" => AppointmentStatus.Pending,
                    "CONFIRMED" => AppointmentStatus.Confirmed,
                    "CONFIRMED_BY_CLIENT" =>
                        AppointmentStatus.ConfirmedByClient,
                    "RESCHEDULE_REQUESTED" =>
                        AppointmentStatus.RescheduleRequested,
                    "CANCELLED_BY_CLIENT" =>
                        AppointmentStatus.CancelledByClient,
                    "CANCELLED_BY_BUSINESS" =>
                        AppointmentStatus.CancelledByBusiness,
                    "COMPLETED" => AppointmentStatus.Completed,
                    "NO_SHOW" => AppointmentStatus.NoShow,
                    "EXPIRED" => AppointmentStatus.Expired,
                    _ => null
                };

                if (status is not null && parsedStatus is null)
                {
                    return Results.BadRequest();
                }

                var query = new GetAppointmentsQuery(
                    LocationId: locationId,
                    ProfessionalId: professionalId,
                    CustomerId: customerId,
                    Status: parsedStatus,
                    From: from,
                    To: to,
                    Page: page ?? 1,
                    PageSize: pageSize ?? 50);

                var result = await handler.HandleAsync(
                    query,
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    return Results.BadRequest();
                }

                return Results.Ok(result.Response);
            });

        group.MapPost(
            "/{appointmentId:guid}/cancel",
            async (
                Guid appointmentId,
                CancelAppointmentRequest request,
                [FromHeader(Name = "Idempotency-Key")]
                string? idempotencyKey,
                CancelAppointmentHandler handler,
                CancellationToken cancellationToken) =>
            {
                AppointmentStatus? cancellationStatus =
                    request.CancelledBy switch
                    {
                        "CLIENT" =>
                            AppointmentStatus.CancelledByClient,
                        "BUSINESS" =>
                            AppointmentStatus.CancelledByBusiness,
                        _ => null
                    };

                if (cancellationStatus is null)
                {
                    return Results.BadRequest();
                }

                var result = await handler.HandleAsync(
                    new CancelAppointmentCommand(
                        appointmentId,
                        cancellationStatus.Value,
                        request.Reason ?? string.Empty,
                        idempotencyKey ?? string.Empty),
                    cancellationToken);

                if (result.IsSuccess && result.Response is { } response)
                {
                    return Results.Ok(response);
                }

                return result.Error switch
                {
                    CancelAppointmentError.InvalidRequest =>
                        Results.BadRequest(),

                    CancelAppointmentError.AppointmentNotFound =>
                        Results.NotFound(),

                    CancelAppointmentError.InvalidState =>
                        Conflict(
                            "INVALID_APPOINTMENT_STATE",
                            "The current state or cancellation data " +
                            "does not allow this operation."),

                    CancelAppointmentError.ReservationExpired =>
                        Conflict(
                            "RESERVATION_EXPIRED",
                            "The reservation has expired."),

                    CancelAppointmentError.IdempotencyConflict =>
                        Conflict(
                            "IDEMPOTENCY_CONFLICT",
                            "The key was already used with different content."),

                    CancelAppointmentError.ConcurrencyConflict =>
                        Conflict(
                            "CONCURRENCY_CONFLICT",
                            "The appointment changed during the operation. " +
                            "Reload it and retry."),

                    _ => Results.Problem(
                        title: "Appointment cancellation failed",
                        statusCode:
                            StatusCodes.Status500InternalServerError)
                };
            });

        group.MapPost(
            "/{appointmentId:guid}/reschedule",
            async (
                Guid appointmentId,
                RescheduleAppointmentRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                RescheduleAppointmentHandler handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.HandleAsync(
                    new RescheduleAppointmentCommand(
                        appointmentId,
                        request.LocationId,
                        request.ProfessionalId,
                        request.StartsAt,
                        idempotencyKey ?? string.Empty),
                    cancellationToken);

                if (result.IsSuccess && result.Response is { } response)
                {
                    return Results.Ok(response);
                }

                return result.Error switch
                {
                    RescheduleAppointmentError.InvalidRequest =>
                        Results.BadRequest(),

                    RescheduleAppointmentError.AppointmentNotFound or
                    RescheduleAppointmentError.LocationNotFound or
                    RescheduleAppointmentError.BusinessNotFound or
                    RescheduleAppointmentError.ProfessionalNotFound or
                    RescheduleAppointmentError.ServiceNotFound or
                    RescheduleAppointmentError.ProfessionalLocationNotFound =>
                        Results.NotFound(),

                    RescheduleAppointmentError.InvalidState =>
                        Conflict(
                            "INVALID_APPOINTMENT_STATE",
                            "The current appointment state does not allow rescheduling."),

                    RescheduleAppointmentError.ReservationExpired =>
                        Conflict(
                            "RESERVATION_EXPIRED",
                            "The reservation has expired."),

                    RescheduleAppointmentError.SlotUnavailable =>
                        Conflict(
                            "SLOT_UNAVAILABLE",
                            "The requested slot is unavailable."),

                    RescheduleAppointmentError.IdempotencyConflict =>
                        Conflict(
                            "IDEMPOTENCY_CONFLICT",
                            "The key was already used with different content."),

                    RescheduleAppointmentError.ConcurrencyConflict =>
                        Conflict(
                            "CONCURRENCY_CONFLICT",
                            "The appointment was modified by another operation."),

                    RescheduleAppointmentError.InactiveLocation or
                    RescheduleAppointmentError.InactiveBusiness or
                    RescheduleAppointmentError.InactiveProfessional or
                    RescheduleAppointmentError.LocationFromAnotherBusiness or
                    RescheduleAppointmentError.ProfessionalFromAnotherBusiness or
                    RescheduleAppointmentError.InactiveProfessionalLocation or
                    RescheduleAppointmentError.InvalidServices or
                    RescheduleAppointmentError.ServicesNotOffered or
                    RescheduleAppointmentError.ProfessionalNotEligible =>
                        Conflict(
                            "APPOINTMENT_CONFLICT",
                            "Resources are incompatible with the appointment."),

                    RescheduleAppointmentError.InvalidTimezone =>
                        Results.Problem(
                            title: "Appointment configuration error",
                            statusCode:
                                StatusCodes.Status500InternalServerError),

                    _ => Results.Problem(
                        title: "Appointment rescheduling failed",
                        statusCode:
                            StatusCodes.Status500InternalServerError)
                };
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

    public sealed record CancelAppointmentRequest(
        string? CancelledBy,
        string? Reason);

    public sealed record RescheduleAppointmentRequest(
        Guid LocationId,
        Guid ProfessionalId,
        DateTimeOffset StartsAt);
}