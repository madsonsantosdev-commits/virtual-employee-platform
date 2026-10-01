using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Domain.Tests.Services;

public sealed class ServiceTests
{
    [Fact]
    public void Constructor_WithValidData_ShouldCreateService()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var service = new Service(
            id,
            tenantId,
            businessId,
            "  Corte Masculino  ",
            ServiceType.Single,
            50.00m,
            30,
            createdAt);

        Assert.Equal(id, service.Id);
        Assert.Equal(tenantId, service.TenantId);
        Assert.Equal(businessId, service.BusinessId);
        Assert.Equal("Corte Masculino", service.Name);
        Assert.Equal(ServiceType.Single, service.ServiceType);
        Assert.Equal(50.00m, service.Price);
        Assert.Equal(30, service.DurationMinutes);
        Assert.True(service.IsActive);
        Assert.Equal(createdAt, service.CreatedAt);
        Assert.Equal(createdAt, service.UpdatedAt);
    }

    [Fact]
    public void Constructor_WithZeroPrice_ShouldCreateService()
    {
        var service = CreateService(price: 0m);

        Assert.Equal(0m, service.Price);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateService(id: Guid.Empty));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateService(tenantId: Guid.Empty));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyBusinessId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateService(businessId: Guid.Empty));

        Assert.Equal("businessId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidServiceType_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateService(serviceType: (ServiceType)999));

        Assert.Equal("serviceType", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithBlankName_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateService(name: "   "));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNegativePrice_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateService(price: -0.01m));

        Assert.Equal("price", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNonPositiveDuration_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateService(durationMinutes: 0));

        Assert.Equal("durationMinutes", exception.ParamName);
    }

    [Fact]
    public void Update_WithValidData_ShouldUpdateMutableFields()
    {
        var service = CreateService();
        var updatedAt = service.CreatedAt.AddMinutes(1);

        service.Update(
            "  Corte Premium  ",
            75.00m,
            45,
            false,
            updatedAt);

        Assert.Equal("Corte Premium", service.Name);
        Assert.Equal(75.00m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
        Assert.False(service.IsActive);
        Assert.Equal(updatedAt, service.UpdatedAt);
    }

    [Fact]
    public void Update_ShouldPreserveStructuralFields()
    {
        var service = CreateService(
            serviceType: ServiceType.Combo);

        var id = service.Id;
        var tenantId = service.TenantId;
        var businessId = service.BusinessId;
        var serviceType = service.ServiceType;
        var createdAt = service.CreatedAt;

        service.Update(
            "Combo Premium",
            100.00m,
            60,
            false,
            createdAt.AddMinutes(1));

        Assert.Equal(id, service.Id);
        Assert.Equal(tenantId, service.TenantId);
        Assert.Equal(businessId, service.BusinessId);
        Assert.Equal(serviceType, service.ServiceType);
        Assert.Equal(createdAt, service.CreatedAt);
    }

    [Fact]
    public void Update_WithInvalidData_ShouldThrow()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(
            () => service.Update(
                " ",
                50m,
                30,
                true,
                DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.Update(
                "Corte",
                -1m,
                30,
                true,
                DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => service.Update(
                "Corte",
                50m,
                0,
                true,
                DateTimeOffset.UtcNow));
    }

    private static Service CreateService(
        Guid? id = null,
        Guid? tenantId = null,
        Guid? businessId = null,
        string name = "Corte Masculino",
        ServiceType serviceType = ServiceType.Single,
        decimal price = 50.00m,
        int durationMinutes = 30)
    {
        return new Service(
            id ?? Guid.NewGuid(),
            tenantId ?? Guid.NewGuid(),
            businessId ?? Guid.NewGuid(),
            name,
            serviceType,
            price,
            durationMinutes,
            DateTimeOffset.UtcNow);
    }
}