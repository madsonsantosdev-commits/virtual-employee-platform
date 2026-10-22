using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Application.ProfessionalLocations;
using VirtualEmployee.Application.ProfessionalServices;
using VirtualEmployee.Infrastructure.ProfessionalServices;
using VirtualEmployee.Infrastructure.ProfessionalLocations;
using VirtualEmployee.Infrastructure.Businesses;
using VirtualEmployee.Infrastructure.Locations;
using VirtualEmployee.Infrastructure.LocationServices;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Professionals;
using VirtualEmployee.Infrastructure.Services;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Infrastructure.AvailabilityRules;
using VirtualEmployee.Application.AvailabilitySlots;
using VirtualEmployee.Infrastructure.AvailabilitySlots;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Infrastructure.ScheduleBlocks;
using VirtualEmployee.Application.Customers;
using VirtualEmployee.Infrastructure.Customers;
using VirtualEmployee.Application.BookingQuotes;
using VirtualEmployee.Infrastructure.BookingQuotes;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Infrastructure.Appointments;

namespace VirtualEmployee.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' was not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<TenantContext>();

        services.AddScoped<ITenantContext>(serviceProvider =>
            serviceProvider.GetRequiredService<TenantContext>());

        services.AddScoped<ITenantContextInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<TenantContext>());

        services.AddScoped<IAppointmentCancellationService, AppointmentCancellationService>();
        services.AddOptions<AppointmentReservationOptions>()
            .Configure(options =>
            {
                var section = AppointmentReservationOptions.SectionName;

                var lifetime = configuration[
                    $"{section}:LifetimeMinutes"];

                if (lifetime is not null)
                {
                    options.LifetimeMinutes =
                        int.TryParse(lifetime, out var minutes)
                            ? minutes
                            : 0;
                }

                var interval = configuration[
                    $"{section}:ExpirationIntervalSeconds"];

                if (interval is not null)
                {
                    options.ExpirationIntervalSeconds =
                        int.TryParse(interval, out var seconds)
                            ? seconds
                            : 0;
                }

                var enabled = configuration[
                    $"{section}:ExpirationWorkerEnabled"];

                if (enabled is not null)
                {
                    if (!bool.TryParse(enabled, out var parsedEnabled))
                    {
                        throw new InvalidOperationException(
                            "ExpirationWorkerEnabled must be true or false.");
                    }

                    options.ExpirationWorkerEnabled = parsedEnabled;
                }
            })
            .Validate(
                options => options.LifetimeMinutes > 0,
                "Reservation lifetime must be a positive integer.")
            .Validate(
                options => options.ExpirationIntervalSeconds > 0,
                "Expiration interval must be a positive integer.")
            .ValidateOnStart();

        services.AddHostedService<AppointmentExpirationWorker>();
        services.AddScoped<IAppointmentCreationService,AppointmentCreationService>();
        services.AddScoped<IBusinessReadService, BusinessReadService>();
        services.AddScoped<IBusinessWriteService, BusinessWriteService>();
        services.AddScoped<ILocationReadService, LocationReadService>();
        services.AddScoped<ILocationWriteService, LocationWriteService>();
        services.AddScoped<IServiceReadService, ServiceReadService>();
        services.AddScoped<IServiceWriteService, ServiceWriteService>();
        services.AddScoped<ILocationServiceReadService, LocationServiceReadService>();
        services.AddScoped<ILocationServiceWriteService, LocationServiceWriteService>();
        services.AddScoped<IProfessionalReadService, ProfessionalReadService>();
        services.AddScoped<IProfessionalWriteService, ProfessionalWriteService>();
        services.AddScoped<IProfessionalLocationWriteService, ProfessionalLocationWriteService>();
        services.AddScoped<IProfessionalLocationReadService, ProfessionalLocationReadService>();
        services.AddScoped<IProfessionalServiceReadService, ProfessionalServiceReadService>();
        services.AddScoped<IProfessionalServiceWriteService, ProfessionalServiceWriteService>();
        services.AddScoped<IAvailabilityRuleReadService, AvailabilityRuleReadService>();
        services.AddScoped<IAvailabilityRuleWriteService, AvailabilityRuleWriteService>();
        services.AddScoped<IAvailabilitySlotReadService, AvailabilitySlotReadService>();
        services.AddScoped<IScheduleBlockCreationService, ScheduleBlockCreationService>();
        services.AddScoped<IScheduleBlockReadService, ScheduleBlockReadService>();
        services.AddScoped<IScheduleBlockRemovalService, ScheduleBlockRemovalService>();
        services.AddScoped<ICustomerReadService, CustomerReadService>();
        services.AddScoped<ICustomerWriteService, CustomerWriteService>();
        services.AddScoped<IBookingQuoteReadService, BookingQuoteReadService>();
        services.AddScoped<IAppointmentReadService, AppointmentReadService>();
        services.AddScoped<IAppointmentExpirationService, AppointmentExpirationService>();
        services.AddScoped<IAppointmentCancellationService, AppointmentCancellationService>();
        services.AddScoped<IAppointmentReschedulingService,AppointmentReschedulingService>();
        services.AddScoped<IAppointmentHistoryReadService, AppointmentHistoryReadService>();
        return services;
    }
}