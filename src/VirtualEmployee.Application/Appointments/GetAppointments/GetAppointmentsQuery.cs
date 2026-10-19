using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Application.Appointments.GetAppointments;

public sealed record GetAppointmentsQuery(
    Guid? LocationId = null,
    Guid? ProfessionalId = null,
    Guid? CustomerId = null,
    AppointmentStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);