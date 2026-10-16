using VirtualEmployee.Domain.Customers;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Customers;

public sealed class CustomerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Constructor_ShouldPreserveIdentityAndTrimContactData()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var customer = new Customer(
            id,
            tenantId,
            businessId,
            "  +5511999990001  ",
            CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
            "  Maria Silva  ",
            "  maria@example.com  ");

        Assert.Equal(id, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal(businessId, customer.BusinessId);
        Assert.Equal("Maria Silva", customer.Name);
        Assert.Equal("+5511999990001", customer.Phone);
        Assert.Equal("maria@example.com", customer.Email);
        Assert.Equal(CreatedAt, customer.CreatedAt);
        Assert.Equal(CreatedAt, customer.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, customer.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, customer.UpdatedAt.Offset);
    }

    [Fact]
    public void Update_ShouldChangeContactDataAndPreserveIdentity()
    {
        var customer = CreateCustomer("Maria", "old@example.com");
        var id = customer.Id;
        var tenantId = customer.TenantId;
        var businessId = customer.BusinessId;
        var updatedAt = CreatedAt.AddHours(1);

        customer.Update(
            "  +5511988880002  ",
            updatedAt.ToOffset(TimeSpan.FromHours(-3)),
            "  Maria Silva  ",
            "  new@example.com  ");

        Assert.Equal(id, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal(businessId, customer.BusinessId);
        Assert.Equal("Maria Silva", customer.Name);
        Assert.Equal("+5511988880002", customer.Phone);
        Assert.Equal("new@example.com", customer.Email);
        Assert.Equal(CreatedAt, customer.CreatedAt);
        Assert.Equal(updatedAt, customer.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, customer.UpdatedAt.Offset);
    }

    [Fact]
    public void Update_WithSameNormalizedData_ShouldPreserveUpdatedAt()
    {
        var customer = CreateCustomer("Maria", "maria@example.com");

        customer.Update(
            "  +5511999990001  ",
            CreatedAt.AddHours(1),
            "  Maria  ",
            "  maria@example.com  ");

        Assert.Equal(CreatedAt, customer.UpdatedAt);
    }

    [Fact]
    public void Update_ShouldAllowClearingOptionalFields()
    {
        var customer = CreateCustomer("Maria", "maria@example.com");
        var updatedAt = CreatedAt.AddHours(1);

        customer.Update("+5511999990001", updatedAt);

        Assert.Null(customer.Name);
        Assert.Null(customer.Email);
        Assert.Equal("+5511999990001", customer.Phone);
        Assert.Equal(updatedAt, customer.UpdatedAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Constructor_ShouldNormalizeEmptyOptionalFields(
        string? name,
        string? email)
    {
        var customer = CreateCustomer(name, email);

        Assert.Null(customer.Name);
        Assert.Null(customer.Email);
        Assert.Equal("+5511999990001", customer.Phone);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("tenant")]
    [InlineData("business")]
    public void Constructor_WithEmptyIdentity_ShouldThrow(string field)
    {
        Assert.Throws<ArgumentException>(() =>
            new Customer(
                field == "id" ? Guid.Empty : Guid.NewGuid(),
                field == "tenant" ? Guid.Empty : Guid.NewGuid(),
                field == "business" ? Guid.Empty : Guid.NewGuid(),
                "+5511999990001",
                CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyPhone_ShouldThrow(string? phone)
    {
        Assert.Throws<ArgumentException>(() =>
            new Customer(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                phone!,
                CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithEmptyPhone_ShouldPreserveExistingData(
        string? phone)
    {
        var customer = CreateCustomer("Maria", "maria@example.com");

        Assert.Throws<ArgumentException>(() =>
            customer.Update(
                phone!,
                CreatedAt.AddHours(1),
                "Changed",
                "changed@example.com"));

        Assert.Equal("Maria", customer.Name);
        Assert.Equal("+5511999990001", customer.Phone);
        Assert.Equal("maria@example.com", customer.Email);
        Assert.Equal(CreatedAt, customer.CreatedAt);
        Assert.Equal(CreatedAt, customer.UpdatedAt);
    }

    #endregion

    #region Helpers

    private static Customer CreateCustomer(
        string? name = null,
        string? email = null)
    {
        return new Customer(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "+5511999990001",
            CreatedAt,
            name,
            email);
    }

    #endregion
}