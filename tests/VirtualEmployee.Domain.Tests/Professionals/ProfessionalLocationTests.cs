using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Domain.Tests.Professionals;

public sealed class ProfessionalLocationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldCreateActiveLink()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var link = new ProfessionalLocation(
            tenantId,
            professionalId,
            locationId,
            CreatedAt);

        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(professionalId, link.ProfessionalId);
        Assert.Equal(locationId, link.LocationId);
        Assert.True(link.IsActive);
        Assert.Equal(CreatedAt, link.CreatedAt);
        Assert.Equal(CreatedAt, link.UpdatedAt);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("professionalId")]
    [InlineData("locationId")]
    public void Constructor_WithEmptyId_ShouldThrow(string parameterName)
    {
        var tenantId = parameterName == "tenantId"
            ? Guid.Empty
            : Guid.NewGuid();

        var professionalId = parameterName == "professionalId"
            ? Guid.Empty
            : Guid.NewGuid();

        var locationId = parameterName == "locationId"
            ? Guid.Empty
            : Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProfessionalLocation(
                tenantId,
                professionalId,
                locationId,
                CreatedAt));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Update_ShouldDeactivateAndReactivateWithoutChangingIdentity()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var link = new ProfessionalLocation(
            tenantId,
            professionalId,
            locationId,
            CreatedAt);

        var deactivatedAt = CreatedAt.AddHours(1);

        link.Update(false, deactivatedAt);

        Assert.False(link.IsActive);
        Assert.Equal(deactivatedAt, link.UpdatedAt);
        Assert.Equal(CreatedAt, link.CreatedAt);
        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(professionalId, link.ProfessionalId);
        Assert.Equal(locationId, link.LocationId);

        var reactivatedAt = CreatedAt.AddHours(2);

        link.Update(true, reactivatedAt);

        Assert.True(link.IsActive);
        Assert.Equal(reactivatedAt, link.UpdatedAt);
        Assert.Equal(CreatedAt, link.CreatedAt);
        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(professionalId, link.ProfessionalId);
        Assert.Equal(locationId, link.LocationId);
    }
}