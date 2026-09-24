using VirtualEmployee.Domain.Locations;

namespace VirtualEmployee.Domain.Tests.Locations;

public sealed class LocationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidData_ShouldCreateLocation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var location = new Location(
            id,
            tenantId,
            businessId,
            "Unidade Centro",
            "br",
            "America/Sao_Paulo",
            CreatedAt,
            "+5511999999999",
            "Av. Paulista, 1000");

        Assert.Equal(id, location.Id);
        Assert.Equal(tenantId, location.TenantId);
        Assert.Equal(businessId, location.BusinessId);
        Assert.Equal("Unidade Centro", location.Name);
        Assert.Equal("+5511999999999", location.Phone);
        Assert.Equal("Av. Paulista, 1000", location.Address);
        Assert.Equal("BR", location.CountryCode);
        Assert.Equal("America/Sao_Paulo", location.Timezone);
        Assert.True(location.IsActive);
        Assert.Equal(CreatedAt, location.CreatedAt);
        Assert.Equal(CreatedAt, location.UpdatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.Empty,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Unidade Centro",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyTenantId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.NewGuid(),
                Guid.Empty,
                Guid.NewGuid(),
                "Unidade Centro",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyBusinessId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                "Unidade Centro",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                " ",
                "BR",
                "America/Sao_Paulo",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyCountryCode_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Unidade Centro",
                " ",
                "America/Sao_Paulo",
                CreatedAt));
    }

    [Fact]
    public void Constructor_WithEmptyTimezone_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Location(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Unidade Centro",
                "BR",
                " ",
                CreatedAt));
    }

    [Fact]
    public void Constructor_ShouldNormalizeValues()
    {
        var location = new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Unidade Centro  ",
            " br ",
            "  America/Sao_Paulo  ",
            CreatedAt,
            "  +5511999999999  ",
            "  Av. Paulista, 1000  ");

        Assert.Equal("Unidade Centro", location.Name);
        Assert.Equal("BR", location.CountryCode);
        Assert.Equal("America/Sao_Paulo", location.Timezone);
        Assert.Equal("+5511999999999", location.Phone);
        Assert.Equal("Av. Paulista, 1000", location.Address);
    }

    [Fact]
    public void Constructor_WithBlankOptionalValues_ShouldStoreNull()
    {
        var location = new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Unidade Virtual",
            "BR",
            "America/Sao_Paulo",
            CreatedAt,
            " ",
            null);

        Assert.Null(location.Phone);
        Assert.Null(location.Address);
    }
}