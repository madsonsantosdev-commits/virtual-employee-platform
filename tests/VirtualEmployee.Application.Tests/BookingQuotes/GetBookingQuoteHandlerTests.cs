using VirtualEmployee.Application.BookingQuotes;
using VirtualEmployee.Application.BookingQuotes.GetBookingQuote;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using Xunit;

namespace VirtualEmployee.Application.Tests.BookingQuotes;

public sealed class GetBookingQuoteHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldCalculateTotalsAndPreserveRequestedOrder()
    {
        var readService = new FakeReadService();
        var handler = new GetBookingQuoteHandler(readService);

        // A leitura retorna A e B, mas a requisição pede B e A.
        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [readService.ServiceB.Id, readService.ServiceA.Id]));

        Assert.True(result.IsSuccess);
        Assert.Equal(GetBookingQuoteError.None, result.Error);
        Assert.NotNull(result.Response);

        Assert.Equal(readService.Location.Id, result.Response.LocationId);
        Assert.Equal(80m, result.Response.TotalPrice);
        Assert.Equal(65, result.Response.TotalDurationMinutes);

        Assert.Collection(
            result.Response.Items,
            item =>
            {
                Assert.Equal(readService.ServiceB.Id, item.ServiceId);
                Assert.Equal("Barba", item.Name);
                Assert.Equal(30m, item.Price);
                Assert.Equal(20, item.DurationMinutes);
            },
            item =>
            {
                Assert.Equal(readService.ServiceA.Id, item.ServiceId);
                Assert.Equal("Corte", item.Name);
                Assert.Equal(50m, item.Price);
                Assert.Equal(45, item.DurationMinutes);
            });
    }

    [Fact]
    public async Task HandleAsync_WithCombo_ShouldUseItsOwnPriceAndDuration()
    {
        var readService = new FakeReadService();

        var combo = new Service(
            Guid.NewGuid(),
            readService.Business.TenantId,
            readService.Business.Id,
            "Corte + Barba",
            ServiceType.Combo,
            70m,
            60,
            FakeReadService.CreatedAt);

        readService.Services = [combo];

        var handler = new GetBookingQuoteHandler(readService);

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [combo.Id]));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);

        var item = Assert.Single(result.Response.Items);

        Assert.Equal(combo.Id, item.ServiceId);
        Assert.Equal(70m, item.Price);
        Assert.Equal(60, item.DurationMinutes);
        Assert.Equal(70m, result.Response.TotalPrice);
        Assert.Equal(60, result.Response.TotalDurationMinutes);
    }

    [Fact]
    public async Task HandleAsync_WithFreeService_ShouldAllowZeroTotalPrice()
    {
        var readService = new FakeReadService();

        readService.ServiceA.Update(
            readService.ServiceA.Name,
            0m,
            45,
            true,
            FakeReadService.CreatedAt.AddHours(1));

        var handler = new GetBookingQuoteHandler(readService);

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [readService.ServiceA.Id]));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Single(result.Response.Items);
        Assert.Equal(0m, result.Response.TotalPrice);
        Assert.Equal(45, result.Response.TotalDurationMinutes);
    }

    [Theory]
    [InlineData("empty-location")]
    [InlineData("null-services")]
    [InlineData("empty-services")]
    [InlineData("empty-service-id")]
    [InlineData("duplicate-services")]
    public async Task HandleAsync_WithInvalidRequest_ShouldReject(
        string scenario)
    {
        var readService = new FakeReadService();
        var handler = new GetBookingQuoteHandler(readService);

        var locationId = scenario == "empty-location"
            ? Guid.Empty
            : readService.Location.Id;

        IReadOnlyList<Guid> serviceIds = scenario switch
        {
            "null-services" => null!,
            "empty-services" => Array.Empty<Guid>(),
            "empty-service-id" => [Guid.Empty],
            "duplicate-services" =>
                [readService.ServiceA.Id, readService.ServiceA.Id],
            _ => [readService.ServiceA.Id]
        };

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(locationId, serviceIds));

        Assert.False(result.IsSuccess);
        Assert.Equal(GetBookingQuoteError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
    }

    [Theory]
    [InlineData("missing", GetBookingQuoteError.ServiceNotFound)]
    [InlineData("inactive", GetBookingQuoteError.InvalidServices)]
    [InlineData("other-business", GetBookingQuoteError.InvalidServices)]
    [InlineData("not-offered", GetBookingQuoteError.ServicesNotOffered)]
    public async Task HandleAsync_WithUnavailableService_ShouldReject(
        string scenario,
        GetBookingQuoteError expectedError)
    {
        var readService = new FakeReadService();

        switch (scenario)
        {
            case "missing":
                // Um serviço existe, mas o segundo solicitado não é retornado.
                readService.Services = [readService.ServiceA];
                break;

            case "inactive":
                readService.ServiceB.Update(
                    readService.ServiceB.Name,
                    readService.ServiceB.Price,
                    readService.ServiceB.DurationMinutes,
                    false,
                    FakeReadService.CreatedAt.AddHours(1));
                break;

            case "other-business":
                readService.Services =
                [
                    readService.ServiceA,
                    new Service(
                        readService.ServiceB.Id,
                        readService.Business.TenantId,
                        Guid.NewGuid(),
                        "Outro Business",
                        ServiceType.Single,
                        30m,
                        20,
                        FakeReadService.CreatedAt)
                ];
                break;

            case "not-offered":
                readService.ServicesOffered = false;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var handler = new GetBookingQuoteHandler(readService);

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [readService.ServiceA.Id, readService.ServiceB.Id]));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Response);
    }

    [Theory]
    [InlineData("missing-location", GetBookingQuoteError.LocationNotFound)]
    [InlineData("inactive-location", GetBookingQuoteError.InactiveLocation)]
    [InlineData("missing-business", GetBookingQuoteError.BusinessNotFound)]
    [InlineData("inactive-business", GetBookingQuoteError.InactiveBusiness)]
    public async Task HandleAsync_WithUnavailableLocationOrBusiness_ShouldReject(
        string scenario,
        GetBookingQuoteError expectedError)
    {
        var readService = new FakeReadService();

        switch (scenario)
        {
            case "missing-location":
                readService.LocationExists = false;
                break;

            case "inactive-location":
                readService.Location.Update(
                    readService.Location.Name,
                    readService.Location.CountryCode,
                    readService.Location.Timezone,
                    false,
                    FakeReadService.CreatedAt.AddHours(1));
                break;

            case "missing-business":
                readService.BusinessExists = false;
                break;

            case "inactive-business":
                readService.Business.Update(
                    readService.Business.BusinessTypeId,
                    readService.Business.Name,
                    readService.Business.SlotIntervalMinutes,
                    false,
                    FakeReadService.CreatedAt.AddHours(1));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var handler = new GetBookingQuoteHandler(readService);

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [readService.ServiceA.Id]));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Response);
    }

    [Theory]
    [InlineData("price-overflow")]
    [InlineData("duration-overflow")]
    public async Task HandleAsync_WithTotalsExceedingLimits_ShouldReject(
        string scenario)
    {
        var readService = new FakeReadService();

        if (scenario == "price-overflow")
        {
            readService.ServiceA.Update(
                readService.ServiceA.Name,
                decimal.MaxValue,
                45,
                true,
                FakeReadService.CreatedAt.AddHours(1));

            readService.ServiceB.Update(
                readService.ServiceB.Name,
                1m,
                20,
                true,
                FakeReadService.CreatedAt.AddHours(1));
        }
        else
        {
            readService.ServiceA.Update(
                readService.ServiceA.Name,
                50m,
                int.MaxValue,
                true,
                FakeReadService.CreatedAt.AddHours(1));

            readService.ServiceB.Update(
                readService.ServiceB.Name,
                30m,
                1,
                true,
                FakeReadService.CreatedAt.AddHours(1));
        }

        var handler = new GetBookingQuoteHandler(readService);

        var result = await handler.HandleAsync(
            new GetBookingQuoteQuery(
                readService.Location.Id,
                [readService.ServiceA.Id, readService.ServiceB.Id]));

        Assert.False(result.IsSuccess);
        Assert.Equal(GetBookingQuoteError.InvalidTotals, result.Error);
        Assert.Null(result.Response);
    }

    private sealed class FakeReadService : IBookingQuoteReadService
    {
        public static readonly DateTimeOffset CreatedAt =
            new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        public Location Location { get; }
        public Business Business { get; }
        public Service ServiceA { get; }
        public Service ServiceB { get; }

        public IReadOnlyList<Service> Services { get; set; }
        public bool ServicesOffered { get; set; } = true;
        public bool LocationExists { get; set; } = true;
        public bool BusinessExists { get; set; } = true;
        public FakeReadService()
        {
            var tenantId = Guid.NewGuid();

            Business = new Business(
                Guid.NewGuid(),
                tenantId,
                BusinessTypeIds.Barbershop,
                "Quote Business",
                CreatedAt);

            Location = new Location(
                Guid.NewGuid(),
                tenantId,
                Business.Id,
                "Quote Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt);

            ServiceA = new Service(
                Guid.NewGuid(),
                tenantId,
                Business.Id,
                "Corte",
                ServiceType.Single,
                50m,
                45,
                CreatedAt);

            ServiceB = new Service(
                Guid.NewGuid(),
                tenantId,
                Business.Id,
                "Barba",
                ServiceType.Single,
                30m,
                20,
                CreatedAt);

            Services = [ServiceA, ServiceB];
        }

        public Task<Location?> GetLocationByIdAsync(
            Guid locationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Location?>(
                LocationExists && Location.Id == locationId
                    ? Location
                    : null);
        }

        public Task<Business?> GetBusinessByIdAsync(
            Guid businessId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Business?>(
                BusinessExists && Business.Id == businessId
                    ? Business
                    : null);
        }

        public Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
            IReadOnlyList<Guid> serviceIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Service> services = Services
                .Where(service => serviceIds.Contains(service.Id))
                .ToArray();

            return Task.FromResult(services);
        }

        public Task<bool> AreServicesOfferedAtLocationAsync(
            Guid locationId,
            IReadOnlyList<Guid> serviceIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServicesOffered);
        }
    }
}