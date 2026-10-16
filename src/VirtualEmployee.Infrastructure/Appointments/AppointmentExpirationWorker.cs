using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentExpirationWorker> _logger;
    private readonly AppointmentReservationOptions _options;

    public AppointmentExpirationWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AppointmentExpirationWorker> logger,
        IOptions<AppointmentReservationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.ExpirationWorkerEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.ExpirationIntervalSeconds),
            _timeProvider);

        try
        {
            do
            {
                try
                {
                    await ExpireReservationsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Appointment expiration cycle failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento solicitado pelo host.
        }
    }

    private async Task ExpireReservationsAsync(
        CancellationToken cancellationToken)
    {
        List<Guid> tenantIds;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // Tenants é o catálogo global de fronteiras de propriedade.
            // Nenhuma entidade tenant-owned é consultada neste escopo.
            tenantIds = await context.Tenants
                .AsNoTracking()
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var initializer = scope.ServiceProvider
                    .GetRequiredService<ITenantContextInitializer>();

                initializer.Initialize(tenantId);

                var service = scope.ServiceProvider
                    .GetRequiredService<IAppointmentExpirationService>();

                var count = await service.ExpireDueAsync(
                    _timeProvider.GetUtcNow(),
                    cancellationToken);

                if (count > 0)
                {
                    _logger.LogInformation(
                        "Expired {Count} appointment reservations for tenant {TenantId}.",
                        count,
                        tenantId);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Appointment expiration failed for tenant {TenantId}.",
                    tenantId);
            }
        }
    }
}