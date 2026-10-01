using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Domain.Tests.Services;

public sealed class ServiceComponentTests
{
    [Fact]
    public void Constructor_WithValidData_ShouldCreateComponent()
    {
        var tenantId = Guid.NewGuid();
        var comboServiceId = Guid.NewGuid();
        var componentServiceId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var component = new ServiceComponent(
            tenantId,
            comboServiceId,
            componentServiceId,
            0,
            createdAt);

        Assert.Equal(tenantId, component.TenantId);
        Assert.Equal(comboServiceId, component.ComboServiceId);
        Assert.Equal(componentServiceId, component.ComponentServiceId);
        Assert.Equal(0, component.SortOrder);
        Assert.Equal(createdAt, component.CreatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateComponent(tenantId: Guid.Empty));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyComboServiceId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateComponent(comboServiceId: Guid.Empty));

        Assert.Equal("comboServiceId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyComponentServiceId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateComponent(componentServiceId: Guid.Empty));

        Assert.Equal("componentServiceId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithSameComboAndComponent_ShouldThrow()
    {
        var serviceId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(
            () => CreateComponent(
                comboServiceId: serviceId,
                componentServiceId: serviceId));

        Assert.Equal("componentServiceId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNegativeSortOrder_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateComponent(sortOrder: -1));

        Assert.Equal("sortOrder", exception.ParamName);
    }

    [Fact]
    public void UpdateSortOrder_WithValidValue_ShouldUpdateSortOrder()
    {
        var component = new ServiceComponent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            DateTimeOffset.UtcNow);

        component.UpdateSortOrder(2);

        Assert.Equal(2, component.SortOrder);
    }

    [Fact]
    public void UpdateSortOrder_WithNegativeValue_ShouldThrow()
    {
        var component = new ServiceComponent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => component.UpdateSortOrder(-1));
    }
    private static ServiceComponent CreateComponent(
        Guid? tenantId = null,
        Guid? comboServiceId = null,
        Guid? componentServiceId = null,
        int sortOrder = 0)
    {
        return new ServiceComponent(
            tenantId ?? Guid.NewGuid(),
            comboServiceId ?? Guid.NewGuid(),
            componentServiceId ?? Guid.NewGuid(),
            sortOrder,
            DateTimeOffset.UtcNow);
    }
}