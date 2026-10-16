using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments;
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

        if (appointment is null)
        {
            return null;
        }

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

    private static string ToResponseStatus(AppointmentStatus status)
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
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}