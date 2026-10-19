using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.GetAppointments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentReadService : IAppointmentReadService
{
    private readonly AppDbContext _dbContext;

    public AppointmentReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .Include(entity => entity.Items)
            .SingleOrDefaultAsync(
                entity => entity.Id == appointmentId,
                cancellationToken);

        return appointment is null
            ? null
            : ToResponse(appointment);
    }

    public async Task<GetAppointmentsResponse> GetAllAsync(
        GetAppointmentsQuery query,
        CancellationToken cancellationToken = default)
    {
        var appointments = _dbContext.Appointments
            .AsNoTracking()
            .AsQueryable();

        if (query.LocationId is Guid locationId)
        {
            appointments = appointments.Where(
                appointment => appointment.LocationId == locationId);
        }

        if (query.ProfessionalId is Guid professionalId)
        {
            appointments = appointments.Where(
                appointment =>
                    appointment.ProfessionalId == professionalId);
        }

        if (query.CustomerId is Guid customerId)
        {
            appointments = appointments.Where(
                appointment => appointment.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            appointments = appointments.Where(
                appointment => appointment.Status == status);
        }

        if (query.From is { } from &&
            query.To is { } to)
        {
            appointments = appointments.Where(
                appointment =>
                    appointment.StartsAt < to &&
                    from < appointment.EndsAt);
        }

        var totalCount = await appointments.CountAsync(
            cancellationToken);

        var offset = checked(
            (int)(((long)query.Page - 1) * query.PageSize));

        var page = await appointments
            .OrderBy(appointment => appointment.StartsAt)
            .ThenBy(appointment => appointment.Id)
            .Skip(offset)
            .Take(query.PageSize)
            .Include(appointment => appointment.Items)
            .ToListAsync(cancellationToken);

        return new GetAppointmentsResponse(
            page.Select(ToResponse).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static AppointmentResponse ToResponse(
        Appointment appointment)
    {
        return new AppointmentResponse(
            appointment.Id,
            appointment.BusinessId,
            appointment.LocationId,
            appointment.CustomerId,
            appointment.ProfessionalId,
            appointment.StartsAt,
            appointment.EndsAt,
            ToResponseStatus(appointment.Status),
            appointment.TotalPriceSnapshot,
            appointment.TotalDurationMinutesSnapshot,
            appointment.ReservationExpiresAt,
            appointment.Items
                .OrderBy(item => item.ServiceId)
                .Select(item => new AppointmentItemResponse(
                    item.ServiceId,
                    item.ServiceNameSnapshot,
                    item.PriceSnapshot,
                    item.DurationMinutesSnapshot))
                .ToArray(),
            appointment.CreatedAt,
            appointment.UpdatedAt);
    }

    private static string ToResponseStatus(
        AppointmentStatus status)
    {
        return status switch
        {
            AppointmentStatus.Pending => "PENDING",
            AppointmentStatus.Confirmed => "CONFIRMED",
            AppointmentStatus.ConfirmedByClient => "CONFIRMED_BY_CLIENT",
            AppointmentStatus.RescheduleRequested => "RESCHEDULE_REQUESTED",
            AppointmentStatus.CancelledByClient => "CANCELLED_BY_CLIENT",
            AppointmentStatus.CancelledByBusiness => "CANCELLED_BY_BUSINESS",
            AppointmentStatus.Completed => "COMPLETED",
            AppointmentStatus.NoShow => "NO_SHOW",
            AppointmentStatus.Expired => "EXPIRED",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Appointment status is invalid.")
        };
    }
}