using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Infrastructure.Businesses;
using VirtualEmployee.Infrastructure.Locations;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Infrastructure.Services;
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

        services.AddScoped<ILocationReadService, LocationReadService>();
        services.AddScoped<ILocationWriteService, LocationWriteService>();
        services.AddScoped<IBusinessReadService, BusinessReadService>();
        services.AddScoped<IBusinessWriteService, BusinessWriteService>();
        services.AddScoped<ILocationReadService, LocationReadService>();
        services.AddScoped<ILocationWriteService, LocationWriteService>();
        services.AddScoped<IServiceReadService, ServiceReadService>();
        services.AddScoped<IServiceWriteService, ServiceWriteService>();

        return services;
    }
}