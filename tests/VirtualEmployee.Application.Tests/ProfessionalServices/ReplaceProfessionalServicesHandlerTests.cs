using VirtualEmployee.Application.ProfessionalServices;
using VirtualEmployee.Application.ProfessionalServices.ReplaceProfessionalServices;
using VirtualEmployee.Domain.Professionals;
using VirtualEmployee.Domain.Services;
using Xunit;

namespace VirtualEmployee.Application.Tests.ProfessionalServices;

public sealed class ReplaceProfessionalServicesHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithValidServices_ShouldReplaceServices()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, businessId);

        var serviceA = CreateService(tenantId, businessId);
        var serviceB = CreateService(tenantId, businessId);

        var writeService = new FakeProfessionalServiceWriteService
        {
            Professional = professional,
            Services = [serviceA, serviceB]
        };

        var handler = new ReplaceProfessionalServicesHandler(writeService);
        var before = DateTimeOffset.UtcNow;

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                professional.Id,
                [serviceA.Id, serviceB.Id]));

        var after = DateTimeOffset.UtcNow;

        Assert.True(result.IsSuccess);
        Assert.Equal(ProfessionalServiceOperationError.None, result.Error);
        Assert.Equal(professional.Id, writeService.ReplacedProfessionalId);
        Assert.Equal(
            new[] { serviceA.Id, serviceB.Id },
            writeService.ReplacedServiceIds);
        Assert.NotNull(writeService.ReplacedAt);
        Assert.InRange(writeService.ReplacedAt.Value, before, after);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyServiceList_ShouldReplaceWithEmptyList()
    {
        var professional = CreateProfessional(
            Guid.NewGuid(),
            Guid.NewGuid());

        var writeService = new FakeProfessionalServiceWriteService
        {
            Professional = professional
        };

        var handler = new ReplaceProfessionalServicesHandler(writeService);

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                professional.Id,
                []));

        Assert.True(result.IsSuccess);
        Assert.Equal(professional.Id, writeService.ReplacedProfessionalId);
        Assert.NotNull(writeService.ReplacedServiceIds);
        Assert.Empty(writeService.ReplacedServiceIds);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownProfessional_ShouldReturnProfessionalNotFound()
    {
        var writeService = new FakeProfessionalServiceWriteService();
        var handler = new ReplaceProfessionalServicesHandler(writeService);

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                Guid.NewGuid(),
                [Guid.NewGuid()]));

        AssertFailure(
            result,
            ProfessionalServiceOperationError.ProfessionalNotFound,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownService_ShouldReturnServiceNotFound()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, businessId);
        var existingService = CreateService(tenantId, businessId);

        var writeService = new FakeProfessionalServiceWriteService
        {
            Professional = professional,
            Services = [existingService]
        };

        var handler = new ReplaceProfessionalServicesHandler(writeService);

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                professional.Id,
                [existingService.Id, Guid.NewGuid()]));

        AssertFailure(
            result,
            ProfessionalServiceOperationError.ServiceNotFound,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithServiceFromAnotherBusiness_ShouldReturnError()
    {
        var tenantId = Guid.NewGuid();
        var professional = CreateProfessional(tenantId, Guid.NewGuid());
        var service = CreateService(tenantId, Guid.NewGuid());

        var writeService = new FakeProfessionalServiceWriteService
        {
            Professional = professional,
            Services = [service]
        };

        var handler = new ReplaceProfessionalServicesHandler(writeService);

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                professional.Id,
                [service.Id]));

        AssertFailure(
            result,
            ProfessionalServiceOperationError.ServiceFromAnotherBusiness,
            writeService);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateServiceIds_ShouldReturnDuplicateService()
    {
        var professional = CreateProfessional(
            Guid.NewGuid(),
            Guid.NewGuid());

        var serviceId = Guid.NewGuid();

        var writeService = new FakeProfessionalServiceWriteService
        {
            Professional = professional
        };

        var handler = new ReplaceProfessionalServicesHandler(writeService);

        var result = await handler.HandleAsync(
            new ReplaceProfessionalServicesCommand(
                professional.Id,
                [serviceId, serviceId]));

        AssertFailure(
            result,
            ProfessionalServiceOperationError.DuplicateService,
            writeService);
    }

    private static void AssertFailure(
        ProfessionalServiceOperationResult result,
        ProfessionalServiceOperationError expectedError,
        FakeProfessionalServiceWriteService writeService)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(writeService.ReplacedProfessionalId);
        Assert.Null(writeService.ReplacedServiceIds);
        Assert.False(writeService.SaveChangesCalled);
    }

    private static Professional CreateProfessional(
        Guid tenantId,
        Guid businessId)
    {
        return new Professional(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Professional",
            CreatedAt);
    }

    private static Service CreateService(
        Guid tenantId,
        Guid businessId)
    {
        return new Service(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Service",
            ServiceType.Single,
            100m,
            60,
            CreatedAt);
    }

    private sealed class FakeProfessionalServiceWriteService
        : IProfessionalServiceWriteService
    {
        public Professional? Professional { get; init; }

        public IReadOnlyList<Service> Services { get; init; } = [];

        public Guid? ReplacedProfessionalId { get; private set; }

        public IReadOnlyList<Guid>? ReplacedServiceIds { get; private set; }

        public DateTimeOffset? ReplacedAt { get; private set; }

        public bool SaveChangesCalled { get; private set; }

        public Task<Professional?> GetProfessionalByIdAsync(
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Professional is not null &&
                Professional.Id == professionalId
                    ? Professional
                    : null);
        }

        public Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
            IReadOnlyCollection<Guid> serviceIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Service> services = Services
                .Where(service => serviceIds.Contains(service.Id))
                .ToArray();

            return Task.FromResult(services);
        }

        public Task ReplaceAsync(
            Guid professionalId,
            IReadOnlyCollection<Guid> serviceIds,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            ReplacedProfessionalId = professionalId;
            ReplacedServiceIds = serviceIds.ToArray();
            ReplacedAt = updatedAt;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;

            return Task.CompletedTask;
        }
    }
}