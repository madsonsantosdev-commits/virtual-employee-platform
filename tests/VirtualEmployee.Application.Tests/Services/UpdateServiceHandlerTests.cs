using VirtualEmployee.Application.Services;
using VirtualEmployee.Application.Services.UpdateService;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Tests.Services;

public sealed class UpdateServiceHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithSingleService_ShouldUpdateService()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var service = CreateService(
            tenantId,
            businessId,
            ServiceType.Single,
            "Corte",
            50m,
            30);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = service
        };

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            service.Id,
            "Corte Premium",
            65m,
            45,
            false,
            []);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Service);

        Assert.Equal("Corte Premium", service.Name);
        Assert.Equal(65m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
        Assert.False(service.IsActive);

        Assert.Equal(ServiceType.Single, service.ServiceType);
        Assert.Null(writeService.ReplacedComboServiceId);
        Assert.Equal(1, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenServiceDoesNotExist_ShouldReturnServiceNotFound()
    {
        var writeService = new FakeServiceWriteService();

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            Guid.NewGuid(),
            "Corte",
            50m,
            30,
            true,
            []);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ServiceNotFound,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithSingleContainingComponents_ShouldReturnInvalidCombo()
    {
        var service = CreateService(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.Single,
            "Corte",
            50m,
            30);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = service
        };

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            service.Id,
            "Corte",
            50m,
            30,
            true,
            [Guid.NewGuid()]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.InvalidCombo,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithComboWithoutComponents_ShouldReturnInvalidCombo()
    {
        var service = CreateService(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.Combo,
            "Combo",
            80m,
            60);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = service
        };

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            service.Id,
            "Combo",
            80m,
            60,
            true,
            []);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.InvalidCombo,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateComponents_ShouldReturnComboComponentInvalid()
    {
        var service = CreateService(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.Combo,
            "Combo",
            80m,
            60);

        var componentId = Guid.NewGuid();

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = service
        };

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            service.Id,
            "Combo",
            80m,
            60,
            true,
            [componentId, componentId]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ComboComponentInvalid,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithMissingComponent_ShouldReturnComboComponentInvalid()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var combo = CreateService(
            tenantId,
            businessId,
            ServiceType.Combo,
            "Combo",
            80m,
            60);

        var component = CreateService(
            tenantId,
            businessId,
            ServiceType.Single,
            "Corte",
            50m,
            30);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = combo
        };

        writeService.AddExistingService(component);

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            combo.Id,
            "Combo",
            80m,
            60,
            true,
            [
                component.Id,
                Guid.NewGuid()
            ]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ComboComponentInvalid,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithComponentFromAnotherBusiness_ShouldReturnComboComponentInvalid()
    {
        var tenantId = Guid.NewGuid();

        var combo = CreateService(
            tenantId,
            Guid.NewGuid(),
            ServiceType.Combo,
            "Combo",
            80m,
            60);

        var foreignComponent = CreateService(
            tenantId,
            Guid.NewGuid(),
            ServiceType.Single,
            "Outro serviço",
            50m,
            30);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = combo
        };

        writeService.AddExistingService(foreignComponent);

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            combo.Id,
            "Combo",
            80m,
            60,
            true,
            [foreignComponent.Id]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ComboComponentInvalid,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithNestedCombo_ShouldReturnComboNestingNotAllowed()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var combo = CreateService(
            tenantId,
            businessId,
            ServiceType.Combo,
            "Combo principal",
            100m,
            60);

        var nestedCombo = CreateService(
            tenantId,
            businessId,
            ServiceType.Combo,
            "Outro combo",
            70m,
            45);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = combo
        };

        writeService.AddExistingService(nestedCombo);

        var handler = new UpdateServiceHandler(writeService);

        var command = new UpdateServiceCommand(
            combo.Id,
            "Combo principal",
            100m,
            60,
            true,
            [nestedCombo.Id]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ComboNestingNotAllowed,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithValidCombo_ShouldReplaceComponentsPreservingOrder()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var combo = CreateService(
            tenantId,
            businessId,
            ServiceType.Combo,
            "Corte + Barba",
            80m,
            60);

        var firstComponent = CreateService(
            tenantId,
            businessId,
            ServiceType.Single,
            "Corte",
            50m,
            30);

        var secondComponent = CreateService(
            tenantId,
            businessId,
            ServiceType.Single,
            "Barba",
            35m,
            20);

        var writeService = new FakeServiceWriteService
        {
            ServiceToReturn = combo
        };

        writeService.AddExistingService(firstComponent);
        writeService.AddExistingService(secondComponent);

        var handler = new UpdateServiceHandler(writeService);

        var requestedOrder = new[]
        {
            secondComponent.Id,
            firstComponent.Id
        };

        var command = new UpdateServiceCommand(
            combo.Id,
            "Combo Premium",
            95m,
            70,
            true,
            requestedOrder);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Service);

        Assert.Equal("Combo Premium", combo.Name);
        Assert.Equal(95m, combo.Price);
        Assert.Equal(70, combo.DurationMinutes);
        Assert.Equal(ServiceType.Combo, combo.ServiceType);

        Assert.Equal(
            combo.Id,
            writeService.ReplacedComboServiceId);

        Assert.NotNull(
            writeService.ReplacedComponentServiceIds);

        Assert.Equal(
            requestedOrder,
            writeService.ReplacedComponentServiceIds);

        Assert.Equal(
            requestedOrder,
            result.Service.ComponentServiceIds);

        Assert.Equal(1, writeService.SaveChangesCallCount);
    }

    private static Service CreateService(
        Guid tenantId,
        Guid businessId,
        ServiceType serviceType,
        string name,
        decimal price,
        int durationMinutes)
    {
        return new Service(
            Guid.NewGuid(),
            tenantId,
            businessId,
            name,
            serviceType,
            price,
            durationMinutes,
            DateTimeOffset.UtcNow);
    }
}