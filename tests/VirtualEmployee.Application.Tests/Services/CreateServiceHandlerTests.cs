using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Application.Services.CreateService;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Application.Tests.Services;

public sealed class CreateServiceHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithSingleService_ShouldCreateService()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var tenantContext = CreateTenantContext(tenantId);
        var writeService = new FakeServiceWriteService();

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            businessId,
            "Corte masculino",
            ServiceType.Single,
            50m,
            30,
            []);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceOperationError.None, result.Error);

        Assert.NotNull(result.Service);
        Assert.Equal(businessId, result.Service.BusinessId);
        Assert.Equal("Corte masculino", result.Service.Name);
        Assert.Equal(ServiceType.Single, result.Service.ServiceType);
        Assert.Equal(50m, result.Service.Price);
        Assert.Equal(30, result.Service.DurationMinutes);
        Assert.True(result.Service.IsActive);
        Assert.Empty(result.Service.ComponentServiceIds);

        Assert.NotNull(writeService.AddedService);
        Assert.Equal(tenantId, writeService.AddedService.TenantId);
        Assert.Empty(writeService.AddedComponents);
        Assert.Equal(1, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithCombo_ShouldCreateServiceAndComponents()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var firstComponent = CreateSingleService(
            tenantId,
            businessId,
            "Corte");

        var secondComponent = CreateSingleService(
            tenantId,
            businessId,
            "Barba");

        var tenantContext = CreateTenantContext(tenantId);
        var writeService = new FakeServiceWriteService();

        writeService.AddExistingService(firstComponent);
        writeService.AddExistingService(secondComponent);

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var componentIds = new[]
        {
            firstComponent.Id,
            secondComponent.Id
        };

        var command = new CreateServiceCommand(
            businessId,
            "Corte + Barba",
            ServiceType.Combo,
            80m,
            60,
            componentIds);

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Service);

        Assert.Equal(
            ServiceType.Combo,
            result.Service.ServiceType);

        Assert.Equal(
            componentIds,
            result.Service.ComponentServiceIds);

        Assert.NotNull(writeService.AddedService);

        Assert.Equal(
            ServiceType.Combo,
            writeService.AddedService.ServiceType);

        Assert.Equal(2, writeService.AddedComponents.Count);

        Assert.Equal(
            firstComponent.Id,
            writeService.AddedComponents[0].ComponentServiceId);

        Assert.Equal(
            0,
            writeService.AddedComponents[0].SortOrder);

        Assert.Equal(
            secondComponent.Id,
            writeService.AddedComponents[1].ComponentServiceId);

        Assert.Equal(
            1,
            writeService.AddedComponents[1].SortOrder);

        Assert.Equal(1, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WhenBusinessDoesNotExist_ShouldReturnBusinessNotFound()
    {
        var tenantContext = CreateTenantContext(Guid.NewGuid());

        var writeService = new FakeServiceWriteService
        {
            BusinessExists = false
        };

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            Guid.NewGuid(),
            "Corte",
            ServiceType.Single,
            50m,
            30,
            []);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.BusinessNotFound,
            result.Error);

        Assert.Null(writeService.AddedService);
        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithSingleContainingComponents_ShouldReturnInvalidCombo()
    {
        var tenantContext = CreateTenantContext(Guid.NewGuid());
        var writeService = new FakeServiceWriteService();

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            Guid.NewGuid(),
            "Corte",
            ServiceType.Single,
            50m,
            30,
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
        var tenantContext = CreateTenantContext(Guid.NewGuid());
        var writeService = new FakeServiceWriteService();

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            Guid.NewGuid(),
            "Combo vazio",
            ServiceType.Combo,
            80m,
            60,
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
        var componentId = Guid.NewGuid();

        var tenantContext = CreateTenantContext(Guid.NewGuid());
        var writeService = new FakeServiceWriteService();

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            Guid.NewGuid(),
            "Combo duplicado",
            ServiceType.Combo,
            80m,
            60,
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

        var existingComponent = CreateSingleService(
            tenantId,
            businessId,
            "Corte");

        var tenantContext = CreateTenantContext(tenantId);
        var writeService = new FakeServiceWriteService();

        writeService.AddExistingService(existingComponent);

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            businessId,
            "Combo",
            ServiceType.Combo,
            80m,
            60,
            [
                existingComponent.Id,
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

        var targetBusinessId = Guid.NewGuid();
        var anotherBusinessId = Guid.NewGuid();

        var component = CreateSingleService(
            tenantId,
            anotherBusinessId,
            "Serviço externo");

        var tenantContext = CreateTenantContext(tenantId);
        var writeService = new FakeServiceWriteService();

        writeService.AddExistingService(component);

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            targetBusinessId,
            "Combo",
            ServiceType.Combo,
            80m,
            60,
            [component.Id]);

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

        var nestedCombo = new Service(
            Guid.NewGuid(),
            tenantId,
            businessId,
            "Combo existente",
            ServiceType.Combo,
            100m,
            60,
            DateTimeOffset.UtcNow);

        var tenantContext = CreateTenantContext(tenantId);
        var writeService = new FakeServiceWriteService();

        writeService.AddExistingService(nestedCombo);

        var handler = new CreateServiceHandler(
            tenantContext,
            writeService);

        var command = new CreateServiceCommand(
            businessId,
            "Combo de combo",
            ServiceType.Combo,
            150m,
            90,
            [nestedCombo.Id]);

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);

        Assert.Equal(
            ServiceOperationError.ComboNestingNotAllowed,
            result.Error);

        Assert.Equal(0, writeService.SaveChangesCallCount);
    }

    private static FakeTenantContext CreateTenantContext(Guid tenantId)
    {
        return new FakeTenantContext(tenantId);
    }

    private static Service CreateSingleService(Guid tenantId, Guid businessId, string name)
    {
        return new Service(
            Guid.NewGuid(),
            tenantId,
            businessId,
            name,
            ServiceType.Single,
            50m,
            30,
            DateTimeOffset.UtcNow);
    }
}