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

        return services;
    }
}