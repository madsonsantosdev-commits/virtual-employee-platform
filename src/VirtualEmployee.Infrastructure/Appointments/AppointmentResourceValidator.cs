using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.BookingQuotes.GetBookingQuote;
using VirtualEmployee.Infrastructure.BookingQuotes;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

internal sealed class AppointmentResourceValidator
{
    private readonly AppDbContext _dbContext;

    public AppointmentResourceValidator(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppointmentResourceValidation> ValidateAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var quoteHandler = new GetBookingQuoteHandler(
            new BookingQuoteReadService(_dbContext));

        var quoteResult = await quoteHandler.HandleAsync(
            new GetBookingQuoteQuery(
                command.LocationId,
                command.ServiceIds),
            cancellationToken);

        if (!quoteResult.IsSuccess)
        {
            return Failure(MapQuoteError(quoteResult.Error));
        }

        var quote = quoteResult.Response
            ?? throw new InvalidOperationException(
                "Successful quote must contain a response.");

        // Limite da coluna numeric(12,2).
        if (quote.TotalPrice > 9_999_999_999.99m)
        {
            return Failure(CreateAppointmentError.InvalidTotals);
        }

        var location = await _dbContext.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == command.LocationId,
                cancellationToken);

        if (location is null)
        {
            return Failure(CreateAppointmentError.LocationNotFound);
        }

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == command.CustomerId,
                cancellationToken);

        if (customer is null)
        {
            return Failure(CreateAppointmentError.CustomerNotFound);
        }

        if (customer.BusinessId != location.BusinessId)
        {
            return Failure(
                CreateAppointmentError.CustomerFromAnotherBusiness);
        }

        var professional = await _dbContext.Professionals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Id == command.ProfessionalId,
                cancellationToken);

        if (professional is null)
        {
            return Failure(CreateAppointmentError.ProfessionalNotFound);
        }

        if (professional.BusinessId != location.BusinessId)
        {
            return Failure(
                CreateAppointmentError.ProfessionalFromAnotherBusiness);
        }

        if (!professional.IsActive)
        {
            return Failure(CreateAppointmentError.InactiveProfessional);
        }

        var link = await _dbContext.ProfessionalLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.LocationId == location.Id &&
                    entity.ProfessionalId == professional.Id,
                cancellationToken);

        if (link is null)
        {
            return Failure(
                CreateAppointmentError.ProfessionalLocationNotFound);
        }

        if (!link.IsActive)
        {
            return Failure(
                CreateAppointmentError.InactiveProfessionalLocation);
        }

        var serviceIds = command.ServiceIds.ToArray();

        var enabledServices = await _dbContext.ProfessionalServices
            .AsNoTracking()
            .Where(entity =>
                entity.ProfessionalId == professional.Id &&
                entity.IsActive &&
                serviceIds.Contains(entity.ServiceId))
            .Select(entity => entity.ServiceId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (enabledServices != serviceIds.Length)
        {
            return Failure(CreateAppointmentError.ProfessionalNotEligible);
        }

        return new AppointmentResourceValidation(
            CreateAppointmentError.None,
            location.BusinessId,
            location.Timezone,
            quote);
    }

    private static AppointmentResourceValidation Failure(
        CreateAppointmentError error)
    {
        return new AppointmentResourceValidation(
            error,
            Guid.Empty,
            null,
            null);
    }

    private static CreateAppointmentError MapQuoteError(
        GetBookingQuoteError error)
    {
        return error switch
        {
            GetBookingQuoteError.InvalidRequest =>
                CreateAppointmentError.InvalidRequest,

            GetBookingQuoteError.LocationNotFound =>
                CreateAppointmentError.LocationNotFound,

            GetBookingQuoteError.InactiveLocation =>
                CreateAppointmentError.InactiveLocation,

            GetBookingQuoteError.BusinessNotFound =>
                CreateAppointmentError.BusinessNotFound,

            GetBookingQuoteError.InactiveBusiness =>
                CreateAppointmentError.InactiveBusiness,

            GetBookingQuoteError.ServiceNotFound =>
                CreateAppointmentError.ServiceNotFound,

            GetBookingQuoteError.InvalidServices =>
                CreateAppointmentError.InvalidServices,

            GetBookingQuoteError.ServicesNotOffered =>
                CreateAppointmentError.ServicesNotOffered,

            GetBookingQuoteError.InvalidTotals =>
                CreateAppointmentError.InvalidTotals,

            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }
}

internal sealed record AppointmentResourceValidation(
    CreateAppointmentError Error,
    Guid BusinessId,
    string? Timezone,
    BookingQuoteResponse? Quote);