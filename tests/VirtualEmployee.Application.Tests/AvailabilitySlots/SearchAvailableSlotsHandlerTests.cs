using VirtualEmployee.Application.AvailabilitySlots;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using Xunit;

namespace VirtualEmployee.Application.Tests.AvailabilitySlots;

public sealed class SearchAvailableSlotsHandlerTests
{
    private static readonly DateOnly SearchDate = new(2026, 10, 19);

    [Fact]
    public async Task HandleAsync_ShouldSumDurationsAndReturnOrderedSlots()
    {
        var readService = new FakeReadService();
        var handler = CreateHandler(
            readService,
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(CreateQuery(readService));

        Assert.True(result.IsSuccess);

        var response = result.Response;
        Assert.NotNull(response);

        Assert.Equal(readService.Location.Id, response.LocationId);
        Assert.Equal(SearchDate, response.Date);
        Assert.Equal("UTC", response.Timezone);
        Assert.Equal(60, response.TotalDurationMinutes);
        Assert.Equal(7, response.Slots.Count);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero),
            response.Slots[0].StartsAt);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.Zero),
            response.Slots[^1].StartsAt);

        Assert.All(response.Slots, slot =>
        {
            Assert.Equal(readService.ProfessionalId, slot.ProfessionalId);
            Assert.Equal(TimeSpan.FromMinutes(60), slot.EndsAt - slot.StartsAt);
            Assert.Equal(TimeSpan.Zero, slot.StartsAt.Offset);
        });

        Assert.Equal(DayOfWeek.Monday, readService.RequestedDay);
        Assert.Equal(
            readService.Services.Select(service => service.Id).ToArray(),
            readService.RequestedServiceIds);
    }

    [Fact]
    public async Task HandleAsync_ShouldExcludeStartedSlots()
    {
        var readService = new FakeReadService();
        var now = new DateTimeOffset(
            2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        var handler = CreateHandler(readService, now);

        var result = await handler.HandleAsync(CreateQuery(readService));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Equal(3, result.Response.Slots.Count);

        Assert.All(
            result.Response.Slots,
            slot => Assert.True(slot.StartsAt > now));

        Assert.Equal(
            now.AddMinutes(20),
            result.Response.Slots[0].StartsAt);
    }

    [Fact]
    public async Task HandleAsync_WithNoEligibleWindows_ShouldReturnEmptySlots()
    {
        var readService = new FakeReadService
        {
            Windows = []
        };

        var handler = CreateHandler(
            readService,
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(CreateQuery(readService));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Equal(60, result.Response.TotalDurationMinutes);
        Assert.Empty(result.Response.Slots);
    }

    [Theory]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("missing-date")]
    [InlineData("null-services")]
    [InlineData("empty-services")]
    [InlineData("empty-service-id")]
    [InlineData("duplicate-services")]
    public async Task HandleAsync_WithInvalidRequest_ShouldReturnValidationError(
        string scenario)
    {
        var readService = new FakeReadService();
        var query = CreateQuery(readService);

        query = scenario switch
        {
            "empty-location" => query with
            {
                LocationId = Guid.Empty
            },
            "empty-professional" => query with
            {
                ProfessionalId = Guid.Empty
            },
            "missing-date" => query with
            {
                Date = DateOnly.MinValue
            },
            "null-services" => query with
            {
                ServiceIds = null!
            },
            "empty-services" => query with
            {
                ServiceIds = []
            },
            "empty-service-id" => query with
            {
                ServiceIds = [Guid.Empty]
            },
            "duplicate-services" => query with
            {
                ServiceIds =
                [
                    readService.Services[0].Id,
                    readService.Services[0].Id
                ]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var handler = CreateHandler(
            readService,
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            SearchAvailableSlotsError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, readService.LocationReadCount);
        Assert.Null(readService.RequestedDay);
    }

    [Theory]
    [InlineData("missing-location", SearchAvailableSlotsError.LocationNotFound)]
    [InlineData("missing-business", SearchAvailableSlotsError.BusinessNotFound)]
    [InlineData("inactive-location", SearchAvailableSlotsError.InactiveLocation)]
    [InlineData("inactive-business", SearchAvailableSlotsError.InactiveBusiness)]
    [InlineData("missing-service", SearchAvailableSlotsError.ServiceNotFound)]
    [InlineData("inactive-service", SearchAvailableSlotsError.InvalidServices)]
    [InlineData("not-offered", SearchAvailableSlotsError.ServicesNotOffered)]
    [InlineData("invalid-timezone", SearchAvailableSlotsError.InvalidTimezone)]
    public async Task HandleAsync_WithUnavailableResource_ShouldReturnError(
        string scenario,
        SearchAvailableSlotsError expectedError)
    {
        var readService = new FakeReadService();
        var query = CreateQuery(readService);
        var updatedAt = new DateTimeOffset(
            2026, 10, 13, 13, 0, 0, TimeSpan.Zero);

        switch (scenario)
        {
            case "missing-location":
                query = query with { LocationId = Guid.NewGuid() };
                break;

            case "missing-business":
                readService.BusinessExists = false;
                break;

            case "inactive-location":
                readService.Location.Update(
                    readService.Location.Name,
                    readService.Location.CountryCode,
                    readService.Location.Timezone,
                    false,
                    updatedAt);
                break;

            case "inactive-business":
                readService.Business.Update(
                    readService.Business.BusinessTypeId,
                    readService.Business.Name,
                    readService.Business.SlotIntervalMinutes,
                    false,
                    updatedAt);
                break;

            case "missing-service":
                query = query with { ServiceIds = [Guid.NewGuid()] };
                break;

            case "inactive-service":
                var service = readService.Services[0];

                service.Update(
                    service.Name,
                    service.Price,
                    service.DurationMinutes,
                    false,
                    updatedAt);
                break;

            case "not-offered":
                readService.ServicesOffered = false;
                break;

            case "invalid-timezone":
                readService.Location.Update(
                    readService.Location.Name,
                    readService.Location.CountryCode,
                    "Invalid/Timezone",
                    true,
                    updatedAt);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var handler = CreateHandler(
            readService,
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(result.Response);

        // A busca de janelas não deve ocorrer após falha de validação.
        Assert.Null(readService.RequestedDay);
    }
    private static SearchAvailableSlotsQuery CreateQuery(
        FakeReadService readService)
    {
        return new SearchAvailableSlotsQuery(
            readService.Location.Id,
            readService.Services.Select(service => service.Id).ToArray(),
            SearchDate);
    }

    private static SearchAvailableSlotsHandler CreateHandler(
        FakeReadService readService,
        DateTimeOffset now)
    {
        return new SearchAvailableSlotsHandler(
            readService,
            new FixedTimeProvider(now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeReadService : IAvailabilitySlotReadService
    {
        public Business Business { get; }
        public bool BusinessExists { get; set; } = true;
        public bool ServicesOffered { get; set; } = true;
        public Location Location { get; }
        public Guid ProfessionalId { get; } = Guid.NewGuid();
        public IReadOnlyList<Service> Services { get; }
        public IReadOnlyList<AvailabilityWindow> Windows { get; set; }

        public DayOfWeek? RequestedDay { get; private set; }
        public Guid[]? RequestedServiceIds { get; private set; }
        public int LocationReadCount { get; private set; }
        public FakeReadService()
        {
            var tenantId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(
                2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

            Business = new Business(
                Guid.NewGuid(),
                tenantId,
                BusinessTypeIds.Barbershop,
                "Business",
                createdAt,
                slotIntervalMinutes: 20);

            Location = new Location(
                Guid.NewGuid(),
                tenantId,
                Business.Id,
                "Location",
                "BR",
                "UTC",
                createdAt);

            var serviceType = Enum.GetValues<ServiceType>().First();

            Services =
            [
                new Service(
                    Guid.NewGuid(), tenantId, Business.Id,
                    "Service A", serviceType, 30m, 20, createdAt),
                new Service(
                    Guid.NewGuid(), tenantId, Business.Id,
                    "Service B", serviceType, 60m, 40, createdAt)
            ];

            Windows =
            [
                new AvailabilityWindow(
                    ProfessionalId,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0))
            ];
        }

        public Task<Location?> GetLocationByIdAsync(
            Guid locationId,
            CancellationToken cancellationToken = default)
        {
            LocationReadCount++;

            return Task.FromResult<Location?>(
                locationId == Location.Id ? Location : null);
        }

        public Task<Business?> GetBusinessByIdAsync(
            Guid businessId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Business?>(
                BusinessExists && businessId == Business.Id
                    ? Business
                    : null);
        }

        public Task<IReadOnlyList<Service>> GetServicesByIdsAsync(
            IReadOnlyList<Guid> serviceIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Service>>(
                Services.Where(service => serviceIds.Contains(service.Id))
                    .ToArray());
        }

        public Task<bool> AreServicesOfferedAtLocationAsync(
            Guid locationId,
            IReadOnlyList<Guid> serviceIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServicesOffered);
        }

        public Task<IReadOnlyList<AvailabilityWindow>> GetEligibleWindowsAsync(
            Guid locationId,
            Guid businessId,
            IReadOnlyList<Guid> serviceIds,
            DayOfWeek dayOfWeek,
            Guid? professionalId,
            CancellationToken cancellationToken = default)
        {
            RequestedDay = dayOfWeek;
            RequestedServiceIds = serviceIds.ToArray();

            return Task.FromResult(Windows);
        }
    }
}