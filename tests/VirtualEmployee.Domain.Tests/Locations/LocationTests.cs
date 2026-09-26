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
    public void Update_WithValidData_ShouldUpdateOperationalFields()
    {
        var location = new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            CreatedAt,
            "+5511999999999",
            "Av. Paulista, 1000");

        var updatedAt =
            new DateTimeOffset(
                2026,
                9,
                25,
                12,
                0,
                0,
                TimeSpan.Zero);

        location.Update(
            "Unidade Moema",
            "US",
            "America/New_York",
            false,
            updatedAt,
            "+12125551234",
            "5th Avenue, 100");

        Assert.Equal("Unidade Moema", location.Name);
        Assert.Equal("+12125551234", location.Phone);
        Assert.Equal("5th Avenue, 100", location.Address);
        Assert.Equal("US", location.CountryCode);
        Assert.Equal("America/New_York", location.Timezone);
        Assert.False(location.IsActive);
        Assert.Equal(updatedAt, location.UpdatedAt);
    }

    [Fact]
    public void Update_ShouldNormalizeValues()
    {
        var location = new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            CreatedAt);

        var updatedAt =
            new DateTimeOffset(
                2026,
                9,
                25,
                12,
                0,
                0,
                TimeSpan.Zero);

        location.Update(
            "  Unidade Moema  ",
            " br ",
            "  America/Sao_Paulo  ",
            true,
            updatedAt,
            "  +5511987654321  ",
            "  Av. Exemplo, 100  ");

        Assert.Equal("Unidade Moema", location.Name);
        Assert.Equal("BR", location.CountryCode);
        Assert.Equal("America/Sao_Paulo", location.Timezone);
        Assert.Equal("+5511987654321", location.Phone);
        Assert.Equal("Av. Exemplo, 100", location.Address);
    }

    [Fact]
    public void Update_WithBlankOptionalValues_ShouldStoreNull()
    {
        var location = new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            CreatedAt,
            "+5511999999999",
            "Av. Paulista, 1000");

        location.Update(
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            true,
            CreatedAt.AddDays(1),
            " ",
            null);

        Assert.Null(location.Phone);
        Assert.Null(location.Address);
    }

    [Fact]
    public void Update_WithEmptyName_ShouldThrow()
    {
        var location = CreateLocation();

        Assert.Throws<ArgumentException>(() =>
            location.Update(
                " ",
                "BR",
                "America/Sao_Paulo",
                true,
                CreatedAt.AddDays(1)));
    }

    [Fact]
    public void Update_WithEmptyCountryCode_ShouldThrow()
    {
        var location = CreateLocation();

        Assert.Throws<ArgumentException>(() =>
            location.Update(
                "Unidade Centro",
                " ",
                "America/Sao_Paulo",
                true,
                CreatedAt.AddDays(1)));
    }

    [Fact]
    public void Update_WithEmptyTimezone_ShouldThrow()
    {
        var location = CreateLocation();

        Assert.Throws<ArgumentException>(() =>
            location.Update(
                "Unidade Centro",
                "BR",
                " ",
                true,
                CreatedAt.AddDays(1)));
    }

    [Fact]
    public void Update_ShouldPreserveStructuralFields()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var location = new Location(
            id,
            tenantId,
            businessId,
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            CreatedAt);

        location.Update(
            "Unidade Atualizada",
            "BR",
            "America/Sao_Paulo",
            false,
            CreatedAt.AddDays(1));

        Assert.Equal(id, location.Id);
        Assert.Equal(tenantId, location.TenantId);
        Assert.Equal(businessId, location.BusinessId);
        Assert.Equal(CreatedAt, location.CreatedAt);
    }

    private static Location CreateLocation()
    {
        return new Location(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Unidade Centro",
            "BR",
            "America/Sao_Paulo",
            CreatedAt);
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