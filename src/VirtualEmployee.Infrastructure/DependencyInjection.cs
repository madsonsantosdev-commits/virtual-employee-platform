using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Infrastructure.Tenancy;
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

        return services;
    }
}