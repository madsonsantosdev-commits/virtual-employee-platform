using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

internal sealed class AppointmentReschedulingResourceValidator
{
    private readonly AppDbContext _dbContext;

    public AppointmentReschedulingResourceValidator(
        AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppointmentReschedulingResourceValidation> ValidateAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var location = await _dbContext.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == command.LocationId,
                cancellationToken);

        if (location is null)
        {
            return Failure(
                RescheduleAppointmentError.LocationNotFound);
        }

        if (location.BusinessId != appointment.BusinessId)
        {
            return Failure(
                RescheduleAppointmentError.LocationFromAnotherBusiness);
        }

        if (!location.IsActive)
        {
            return Failure(
                RescheduleAppointmentError.InactiveLocation);
        }

        var business = await _dbContext.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == appointment.BusinessId,
                cancellationToken);

        if (business is null)
        {
            return Failure(
                RescheduleAppointmentError.BusinessNotFound);
        }

        if (!business.IsActive)
        {
            return Failure(
                RescheduleAppointmentError.InactiveBusiness);
        }

        var professional = await _dbContext.Professionals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == command.ProfessionalId,
                cancellationToken);

        if (professional is null)
        {
            return Failure(
                RescheduleAppointmentError.ProfessionalNotFound);
        }

        if (professional.BusinessId != appointment.BusinessId)
        {
            return Failure(
                RescheduleAppointmentError.ProfessionalFromAnotherBusiness);
        }

        if (!professional.IsActive)
        {
            return Failure(
                RescheduleAppointmentError.InactiveProfessional);
        }

        var professionalLocation = await _dbContext.ProfessionalLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.LocationId == command.LocationId &&
                    entity.ProfessionalId == command.ProfessionalId,
                cancellationToken);

        if (professionalLocation is null)
        {
            return Failure(
                RescheduleAppointmentError.ProfessionalLocationNotFound);
        }

        if (!professionalLocation.IsActive)
        {
            return Failure(
                RescheduleAppointmentError.InactiveProfessionalLocation);
        }

        // O chamador deve carregar os itens do agendamento.
        var serviceIds = appointment.Items
            .Select(item => item.ServiceId)
            .Distinct()
            .ToArray();

        if (serviceIds.Length == 0)
        {
            return Failure(
                RescheduleAppointmentError.InvalidServices);
        }

        var services = await _dbContext.Services
            .AsNoTracking()
            .Where(entity => serviceIds.Contains(entity.Id))
            .ToListAsync(cancellationToken);

        if (services.Count != serviceIds.Length)
        {
            return Failure(
                RescheduleAppointmentError.ServiceNotFound);
        }

        if (services.Any(service =>
                !service.IsActive ||
                service.BusinessId != appointment.BusinessId))
        {
            return Failure(
                RescheduleAppointmentError.InvalidServices);
        }

        var offeredCount = await _dbContext.LocationServices
            .AsNoTracking()
            .Where(entity =>
                entity.LocationId == command.LocationId &&
                serviceIds.Contains(entity.ServiceId))
            .Select(entity => entity.ServiceId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (offeredCount != serviceIds.Length)
        {
            return Failure(
                RescheduleAppointmentError.ServicesNotOffered);
        }

        var enabledCount = await _dbContext.ProfessionalServices
            .AsNoTracking()
            .Where(entity =>
                entity.ProfessionalId == command.ProfessionalId &&
                entity.IsActive &&
                serviceIds.Contains(entity.ServiceId))
            .Select(entity => entity.ServiceId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (enabledCount != serviceIds.Length)
        {
            return Failure(
                RescheduleAppointmentError.ProfessionalNotEligible);
        }

        return new AppointmentReschedulingResourceValidation(
            RescheduleAppointmentError.None,
            location.Timezone,
            serviceIds);
    }

    private static AppointmentReschedulingResourceValidation Failure(
        RescheduleAppointmentError error)
    {
        return new AppointmentReschedulingResourceValidation(
            error,
            null,
            Array.Empty<Guid>());
    }
}

internal sealed record AppointmentReschedulingResourceValidation(
    RescheduleAppointmentError Error,
    string? Timezone,
    IReadOnlyList<Guid> ServiceIds);