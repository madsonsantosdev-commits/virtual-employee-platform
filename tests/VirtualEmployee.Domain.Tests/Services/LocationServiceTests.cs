using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Domain.Tests.Services;

public sealed class LocationServiceTests
{
    [Fact]
    public void Constructor_WithValidValues_ShouldCreateLocationService()
    {
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var locationService = new LocationService(
            tenantId,
            locationId,
            serviceId,
            createdAt);

        Assert.Equal(tenantId, locationService.TenantId);
        Assert.Equal(locationId, locationService.LocationId);
        Assert.Equal(serviceId, locationService.ServiceId);
        Assert.Equal(createdAt, locationService.CreatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new LocationService(
                Guid.Empty,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyLocationId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new LocationService(
                Guid.NewGuid(),
                Guid.Empty,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));

        Assert.Equal("locationId", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyServiceId_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new LocationService(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                DateTimeOffset.UtcNow));

        Assert.Equal("serviceId", exception.ParamName);
    }
}