using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.GetAppointmentHistory;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentHistoryReadService
    : IAppointmentHistoryReadService
{
    private readonly AppDbContext _dbContext;

    public AppointmentHistoryReadService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<GetAppointmentHistoryResponse?> GetAsync(
        GetAppointmentHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var appointmentExists = await _dbContext.Appointments
            .AsNoTracking()
            .AnyAsync(
                appointment => appointment.Id == query.AppointmentId,
                cancellationToken);

        if (!appointmentExists)
        {
            return null;
        }

        var history = _dbContext.AppointmentHistoryEntries
            .AsNoTracking()
            .Where(entry => entry.AppointmentId == query.AppointmentId);

        var totalCount = await history.CountAsync(cancellationToken);

        var offset = checked(
            (int)(((long)query.Page - 1) * query.PageSize));

        var entries = await history
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Skip(offset)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new GetAppointmentHistoryResponse(
            entries.Select(ToResponse).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static AppointmentHistoryEntryResponse ToResponse(
        AppointmentHistoryEntry entry)
    {
        return new AppointmentHistoryEntryResponse(
            entry.Id,
            entry.AppointmentId,
            ToResponseType(entry.Type),
            entry.OccurredAt,
            entry.Before is { } before
                ? ToResponseState(before)
                : null,
            ToResponseState(entry.After),
            entry.Reason);
    }

    private static AppointmentHistoryStateResponse ToResponseState(
        AppointmentHistoryState state)
    {
        return new AppointmentHistoryStateResponse(
            ToResponseStatus(state.Status),
            state.LocationId,
            state.ProfessionalId,
            state.StartsAt,
            state.EndsAt,
            state.ReservationExpiresAt);
    }

    private static string ToResponseType(AppointmentHistoryType type)
    {
        return type switch
        {
            AppointmentHistoryType.Created => "CREATED",
            AppointmentHistoryType.Cancelled => "CANCELLED",
            AppointmentHistoryType.Rescheduled => "RESCHEDULED",
            AppointmentHistoryType.Expired => "EXPIRED",

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Appointment history type is invalid.")
        };
    }

    private static string ToResponseStatus(AppointmentStatus status)
    {
        return status switch
        {
            AppointmentStatus.Pending => "PENDING",
            AppointmentStatus.Confirmed => "CONFIRMED",
            AppointmentStatus.ConfirmedByClient =>
                "CONFIRMED_BY_CLIENT",
            AppointmentStatus.RescheduleRequested =>
                "RESCHEDULE_REQUESTED",
            AppointmentStatus.CancelledByClient =>
                "CANCELLED_BY_CLIENT",
            AppointmentStatus.CancelledByBusiness =>
                "CANCELLED_BY_BUSINESS",
            AppointmentStatus.Completed => "COMPLETED",
            AppointmentStatus.NoShow => "NO_SHOW",
            AppointmentStatus.Expired => "EXPIRED",

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Appointment status is invalid.")
        };
    }
}