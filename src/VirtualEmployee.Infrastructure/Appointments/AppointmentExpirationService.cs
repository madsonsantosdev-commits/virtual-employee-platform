using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentExpirationService
    : IAppointmentExpirationService
{
    private readonly AppDbContext _dbContext;

    public AppointmentExpirationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> ExpireDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var utc = now.ToUniversalTime();

        var updatedAt = new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);

        return _dbContext.Appointments
            .Where(appointment =>
                appointment.Status == AppointmentStatus.Pending &&
                appointment.ReservationExpiresAt != null &&
                appointment.ReservationExpiresAt <= updatedAt)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(
                        appointment => appointment.Status,
                        AppointmentStatus.Expired)
                    .SetProperty(
                        appointment => appointment.UpdatedAt,
                        updatedAt),
                cancellationToken);
    }
}