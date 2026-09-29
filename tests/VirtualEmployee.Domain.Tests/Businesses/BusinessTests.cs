using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;

namespace VirtualEmployee.Domain.Tests.Businesses;

public sealed class BusinessTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidData_ShouldCreateBusiness()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var business = new Business(
            id,
            tenantId,
            BusinessTypeIds.Barbershop,
            "Barbearia Central",
            CreatedAt);

        Assert.Equal(id, business.Id);
        Assert.Equal(tenantId, business.TenantId);
        Assert.Equal(BusinessTypeIds.Barbershop, business.BusinessTypeId);
        Assert.Equal("Barbearia Central", business.Name);
        Assert.Equal(15, business.SlotIntervalMinutes);
        Assert.True(business.IsActive);
        Assert.Equal(CreatedAt, business.CreatedAt);
        Assert.Equal(CreatedAt, business.UpdatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.Empty,
                Guid.NewGuid(),
                BusinessTypeIds.Barbershop,
                "Barbearia Central",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.Empty,
                BusinessTypeIds.Barbershop,
                "Barbearia Central",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyBusinessTypeId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                "Barbearia Central",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.NewGuid(),
                BusinessTypeIds.Barbershop,
                " ",
                CreatedAt));
    }

    [Fact]
    public void Constructor_ShouldTrimName()
    {
        var business = new Business(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BusinessTypeIds.Barbershop,
            "  Barbearia Central  ",
            CreatedAt);

        Assert.Equal("Barbearia Central", business.Name);
    }

    [Fact]
    public void Constructor_WithCustomSlotInterval_ShouldUseProvidedValue()
    {
        var business = new Business(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BusinessTypeIds.Barbershop,
            "Barbearia Central",
            CreatedAt,
            slotIntervalMinutes: 30);

        Assert.Equal(30, business.SlotIntervalMinutes);
    }

    [Fact]
    public void Constructor_WithInvalidSlotInterval_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.NewGuid(),
                BusinessTypeIds.Barbershop,
                "Barbearia Central",
                CreatedAt,
                slotIntervalMinutes: 0));
    }

    [Fact]
public void Update_WithValidData_ShouldUpdateMutableFields()
{
    var createdAt = new DateTimeOffset(
        2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    var updatedAt = new DateTimeOffset(
        2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    var business = new Business(
        Guid.NewGuid(),
        Guid.NewGuid(),
        BusinessTypeIds.Other,
        "Barbearia Original",
        createdAt);

    business.Update(
        BusinessTypeIds.Barbershop,
        "  Barbearia Central  ",
        30,
        false,
        updatedAt);

    Assert.Equal(BusinessTypeIds.Barbershop, business.BusinessTypeId);
    Assert.Equal("Barbearia Central", business.Name);
    Assert.Equal(30, business.SlotIntervalMinutes);
    Assert.False(business.IsActive);
    Assert.Equal(updatedAt, business.UpdatedAt);
}

[Fact]
public void Update_ShouldPreserveStructuralFields()
{
    var id = Guid.NewGuid();
    var tenantId = Guid.NewGuid();
    var createdAt = new DateTimeOffset(
        2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    var business = new Business(
        id,
        tenantId,
        BusinessTypeIds.Other,
        "Barbearia Original",
        createdAt);

    business.Update(
        BusinessTypeIds.Barbershop,
        "Barbearia Central",
        30,
        false,
        createdAt.AddDays(1));

    Assert.Equal(id, business.Id);
    Assert.Equal(tenantId, business.TenantId);
    Assert.Equal(createdAt, business.CreatedAt);
}

[Fact]
    public void Update_WithEmptyBusinessTypeId_ShouldThrow()
    {
        var business = CreateBusiness();

        Assert.Throws<ArgumentException>(() =>
            business.Update(
                Guid.Empty,
                "Barbearia Central",
                15,
                true,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Update_WithBlankName_ShouldThrow()
    {
        var business = CreateBusiness();

        Assert.Throws<ArgumentException>(() =>
            business.Update(
                BusinessTypeIds.Barbershop,
                "   ",
                15,
                true,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Update_WithNonPositiveSlotInterval_ShouldThrow()
    {
        var business = CreateBusiness();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            business.Update(
                BusinessTypeIds.Barbershop,
                "Barbearia Central",
                0,
                true,
                DateTimeOffset.UtcNow));
    }

    private static Business CreateBusiness()
    {
        return new Business(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BusinessTypeIds.Other,
            "Barbearia Teste",
            new DateTimeOffset(
                2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    }
}