using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Domain.Tests.Professionals;

public sealed class ProfessionalTests
{
    [Fact]
    public void Constructor_ShouldCreateActiveProfessional_WhenDataIsValid()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var professional = new Professional(
            id,
            tenantId,
            businessId,
            "  Arthur Silva  ",
            createdAt);

        Assert.Equal(id, professional.Id);
        Assert.Equal(tenantId, professional.TenantId);
        Assert.Equal(businessId, professional.BusinessId);
        Assert.Equal("Arthur Silva", professional.Name);
        Assert.True(professional.IsActive);
        Assert.Equal(createdAt, professional.CreatedAt);
        Assert.Equal(createdAt, professional.UpdatedAt);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Professional(
                Guid.Empty,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Arthur Silva",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTenantIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Professional(
                Guid.NewGuid(),
                Guid.Empty,
                Guid.NewGuid(),
                "Arthur Silva",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBusinessIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Professional(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                "Arthur Silva",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Professional(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "   ",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Update_ShouldChangeOperationalData_AndPreserveIdentity()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var updatedAt = createdAt.AddHours(1);

        var professional = new Professional(
            id,
            tenantId,
            businessId,
            "Arthur Silva",
            createdAt);

        professional.Update(
            "  Arthur Santos  ",
            false,
            updatedAt);

        Assert.Equal(id, professional.Id);
        Assert.Equal(tenantId, professional.TenantId);
        Assert.Equal(businessId, professional.BusinessId);
        Assert.Equal("Arthur Santos", professional.Name);
        Assert.False(professional.IsActive);
        Assert.Equal(createdAt, professional.CreatedAt);
        Assert.Equal(updatedAt, professional.UpdatedAt);
    }
}