using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Application.LocationServices.ReplaceLocationServices;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using Xunit;

namespace VirtualEmployee.Application.Tests.LocationServices;

public sealed class ReplaceLocationServicesHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithValidServices_ShouldReplaceServices()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var serviceA = CreateService(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Service A");

        var serviceB = CreateService(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Service B");

        var writeService = new FakeLocationServiceWriteService
        {
            Location = CreateLocation(
                locationId,
                tenantId,
                businessId),
            Services = [serviceA, serviceB]
        };

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            locationId,
            [serviceA.Id, serviceB.Id]);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(LocationServiceOperationError.None, result.Error);

        Assert.Equal(locationId, writeService.ReplacedLocationId);

        Assert.Equal(
            new[] { serviceA.Id, serviceB.Id },
            writeService.ReplacedServiceIds);

        Assert.NotNull(writeService.ReplacedAt);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyServiceList_ShouldReplaceWithEmptyList()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var writeService = new FakeLocationServiceWriteService
        {
            Location = CreateLocation(
                locationId,
                tenantId,
                businessId)
        };

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            locationId,
            []);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);

        Assert.Equal(locationId, writeService.ReplacedLocationId);
        Assert.Empty(writeService.ReplacedServiceIds!);
        Assert.True(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownLocation_ShouldReturnLocationNotFound()
    {
        var writeService =
            new FakeLocationServiceWriteService();

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            Guid.NewGuid(),
            [Guid.NewGuid()]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            LocationServiceOperationError.LocationNotFound,
            result.Error);

        Assert.Null(writeService.ReplacedLocationId);
        Assert.False(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownService_ShouldReturnServiceNotFound()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var existingService = CreateService(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Existing Service");

        var writeService = new FakeLocationServiceWriteService
        {
            Location = CreateLocation(
                locationId,
                tenantId,
                businessId),
            Services = [existingService]
        };

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            locationId,
            [
                existingService.Id,
                Guid.NewGuid()
            ]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            LocationServiceOperationError.ServiceNotFound,
            result.Error);

        Assert.Null(writeService.ReplacedLocationId);
        Assert.False(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithServiceFromAnotherBusiness_ShouldReturnError()
    {
        var tenantId = Guid.NewGuid();

        var locationBusinessId = Guid.NewGuid();
        var anotherBusinessId = Guid.NewGuid();

        var locationId = Guid.NewGuid();

        var service = CreateService(
            Guid.NewGuid(),
            tenantId,
            anotherBusinessId,
            "Another Business Service");

        var writeService = new FakeLocationServiceWriteService
        {
            Location = CreateLocation(
                locationId,
                tenantId,
                locationBusinessId),
            Services = [service]
        };

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            locationId,
            [service.Id]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            LocationServiceOperationError.ServiceFromAnotherBusiness,
            result.Error);

        Assert.Null(writeService.ReplacedLocationId);
        Assert.False(writeService.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateServiceIds_ShouldReturnDuplicateService()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var writeService = new FakeLocationServiceWriteService
        {
            Location = CreateLocation(
                locationId,
                tenantId,
                businessId)
        };

        var handler =
            new ReplaceLocationServicesHandler(writeService);

        var command = new ReplaceLocationServicesCommand(
            locationId,
            [serviceId, serviceId]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            LocationServiceOperationError.DuplicateService,
            result.Error);

        Assert.Null(writeService.ReplacedLocationId);
        Assert.False(writeService.SaveChangesCalled);
    }

    private static Location CreateLocation(
        Guid id,
        Guid tenantId,
        Guid businessId)
    {
        return new Location(
            id,
            tenantId,
            businessId,
            "Location",
            "BR",
            "America/Sao_Paulo",
            CreatedAt);
    }

    private static Service CreateService(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name)
    {
        return new Service(
            id,
            tenantId,
            businessId,
            name,
            ServiceType.Single,
            100m,
            60,
            CreatedAt);
    }
}