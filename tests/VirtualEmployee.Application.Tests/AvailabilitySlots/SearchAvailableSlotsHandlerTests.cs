using VirtualEmployee.Application.AvailabilitySlots;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Application.ScheduleBlocks;
using Xunit;

namespace VirtualEmployee.Application.Tests.AvailabilitySlots;

public sealed class SearchAvailableSlotsHandlerTests
{
    private static readonly DateOnly SearchDate = new(2026, 10, 19);

    #region Facts

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

    #endregion

    #region Theories

    [Theory]
    [InlineData("overlap", 3)]
    [InlineData("other-professional", 7)]
    [InlineData("ends-at-window-start", 7)]
    [InlineData("starts-at-window-end", 7)]
    public async Task HandleAsync_ShouldExcludeOnlyOverlappingAppointments(
        string scenario,
        int expectedCount)
    {
        var readService = new FakeReadService();

        var occupiedStart = new DateTimeOffset(
            2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        var occupiedEnd = occupiedStart.AddMinutes(30);

        if (scenario == "ends-at-window-start")
        {
            occupiedStart = new DateTimeOffset(
                2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
            occupiedEnd = occupiedStart.AddHours(1);
        }
        else if (scenario == "starts-at-window-end")
        {
            occupiedStart = new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            occupiedEnd = occupiedStart.AddHours(1);
        }

        readService.OccupiedWindows =
        [
            new OccupiedAppointmentWindow(
                scenario == "other-professional"
                    ? Guid.NewGuid()
                    : readService.ProfessionalId,
                occupiedStart,
                occupiedEnd)
        ];

        var handler = CreateHandler(
            readService,
            new DateTimeOffset(
                2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(CreateQuery(readService));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Equal(expectedCount, result.Response.Slots.Count);

        if (scenario == "overlap")
        {
            Assert.Equal(
                new[]
                {
                    new DateTimeOffset(
                        2026, 10, 19, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 10, 40, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 11, 0, 0, TimeSpan.Zero)
                },
                result.Response.Slots
                    .Select(slot => slot.StartsAt)
                    .ToArray());
        }
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

    [Theory]
    [InlineData("location", 3)]
    [InlineData("professional", 3)]
    [InlineData("other-professional", 7)]
    [InlineData("inactive", 7)]
    [InlineData("other-location", 7)]
    [InlineData("ends-at-window-start", 7)]
    [InlineData("starts-at-window-end", 7)]
    public async Task HandleAsync_ShouldExcludeOnlySlotsOverlappingApplicableBlocks(
        string scenario,
        int expectedCount)
    {
        var readService = new FakeReadService();

        var blockStart = new DateTimeOffset(
            2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        var blockEnd = blockStart.AddMinutes(30);

        if (scenario == "ends-at-window-start")
        {
            blockStart = new DateTimeOffset(
                2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
            blockEnd = blockStart.AddHours(1);
        }
        else if (scenario == "starts-at-window-end")
        {
            blockStart = new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            blockEnd = blockStart.AddHours(1);
        }

        Guid? blockProfessionalId = scenario switch
        {
            "professional" => readService.ProfessionalId,
            "other-professional" => Guid.NewGuid(),
            _ => null
        };

        readService.Blocks =
        [
            new ScheduleBlockResponse(
                Guid.NewGuid(),
                scenario == "other-location"
                    ? Guid.NewGuid()
                    : readService.Location.Id,
                blockProfessionalId,
                blockStart,
                blockEnd,
                scenario != "inactive",
                blockStart.AddDays(-1),
                blockStart.AddDays(-1))
        ];

        var handler = CreateHandler(
            readService,
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.HandleAsync(CreateQuery(readService));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Equal(expectedCount, result.Response.Slots.Count);

        if (scenario is "location" or "professional")
        {
            // 09h–10h termina no início do bloqueio e permanece disponível.
            // O próximo início válido na grade é 10h40.
            Assert.Equal(
                new[]
                {
                    new DateTimeOffset(
                        2026, 10, 19, 9, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 10, 40, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 11, 0, 0, TimeSpan.Zero)
                },
                result.Response.Slots.Select(slot => slot.StartsAt).ToArray());
        }
    }

    #endregion

    #region Helpers

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
        public IReadOnlyList<ScheduleBlockResponse> Blocks { get; set; } = [];
        public IReadOnlyList<OccupiedAppointmentWindow> OccupiedWindows {get;set;} = [];
        public DayOfWeek? RequestedDay { get; private set; }
        public Guid[]? RequestedServiceIds { get; private set; }
        public int LocationReadCount { get; private set; }
        public Task<IReadOnlyList<ScheduleBlockResponse>> GetBlocksAsync(
            Guid locationId,
            DateTimeOffset from,
            DateTimeOffset to,
            Guid? professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Blocks);
        }
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

        public Task<IReadOnlyList<OccupiedAppointmentWindow>>
            GetOccupiedAppointmentsAsync(
                IReadOnlyList<Guid> professionalIds,
                DateTimeOffset from,
                DateTimeOffset to,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OccupiedWindows);
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

    #endregion
}