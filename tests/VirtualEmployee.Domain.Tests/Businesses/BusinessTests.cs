using VirtualEmployee.Domain.Businesses;

namespace VirtualEmployee.Domain.Tests.Businesses;

public sealed class BusinessTests
{
    [Fact]
    public void Constructor_WithValidData_ShouldCreateBusiness()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var business = new Business(
            id,
            tenantId,
            "Barbearia Central");

        Assert.Equal(id, business.Id);
        Assert.Equal(tenantId, business.TenantId);
        Assert.Equal("Barbearia Central", business.Name);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.Empty,
                Guid.NewGuid(),
                "Barbearia Central"));
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.Empty,
                "Barbearia Central"));
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Business(
                Guid.NewGuid(),
                Guid.NewGuid(),
                " "));
    }

    [Fact]
    public void Constructor_ShouldTrimName()
    {
        var business = new Business(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Barbearia Central  ");

        Assert.Equal("Barbearia Central", business.Name);
    }
}