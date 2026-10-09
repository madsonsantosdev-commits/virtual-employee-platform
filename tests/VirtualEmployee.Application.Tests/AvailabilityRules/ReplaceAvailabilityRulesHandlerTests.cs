using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Application.AvailabilityRules.ReplaceAvailabilityRules;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Professionals;
using Xunit;

namespace VirtualEmployee.Application.Tests.AvailabilityRules;

public sealed class ReplaceAvailabilityRulesHandlerTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_WithAdjacentWindows_ShouldReplaceOrderedRules()
    {
        var service = new FakeWriteService();
        var handler = new ReplaceAvailabilityRulesHandler(service);

        var morning = Window(1, 9, 12);
        var afternoon = Window(1, 12, 18);
        var tuesday = Window(2, 9, 12);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                [tuesday, afternoon, morning]));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            AvailabilityRuleOperationError.None,
            result.Error);
        Assert.Equal(service.Location.Id, service.ReplacedLocationId);
        Assert.Equal(
            service.Professional.Id,
            service.ReplacedProfessionalId);
        Assert.Equal(
            new[] { morning, afternoon, tuesday },
            service.ReplacedRules);
        Assert.NotNull(service.ReplacedAt);
        Assert.True(service.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyList_ShouldReplaceWithEmptyAgenda()
    {
        var service = new FakeWriteService();
        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                []));

        Assert.True(result.IsSuccess);
        Assert.NotNull(service.ReplacedRules);
        Assert.Empty(service.ReplacedRules);
        Assert.True(service.SaveChangesCalled);
    }

        [Theory]
    [InlineData(true, AvailabilityRuleOperationError.LocationNotFound)]
    [InlineData(false, AvailabilityRuleOperationError.ProfessionalNotFound)]
    public async Task HandleAsync_WithUnknownResource_ShouldRejectWithoutWriting(
        bool unknownLocation,
        AvailabilityRuleOperationError expectedError)
    {
        var service = new FakeWriteService();
        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                unknownLocation
                    ? Guid.NewGuid()
                    : service.Location.Id,
                unknownLocation
                    ? service.Professional.Id
                    : Guid.NewGuid(),
                [Window(1, 9, 12)]));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(service.ReplacedRules);
        Assert.False(service.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleAsync_WithInactiveLink_ShouldRejectWithoutWriting()
    {
        var service = new FakeWriteService();
        service.Link.Update(false, CreatedAt.AddHours(1));

        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                [Window(1, 9, 12)]));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            AvailabilityRuleOperationError.InactiveProfessionalLocation,
            result.Error);
        Assert.Null(service.ReplacedRules);
        Assert.False(service.SaveChangesCalled);
    }

    [Theory]
    [InlineData(-1, 9, 12, AvailabilityRuleOperationError.InvalidDayOfWeek)]
    [InlineData(7, 9, 12, AvailabilityRuleOperationError.InvalidDayOfWeek)]
    [InlineData(1, 9, 9, AvailabilityRuleOperationError.InvalidTimeWindow)]
    [InlineData(1, 12, 9, AvailabilityRuleOperationError.InvalidTimeWindow)]
    public async Task HandleAsync_WithInvalidWindow_ShouldRejectWithoutWriting(
        int day,
        int startHour,
        int endHour,
        AvailabilityRuleOperationError expectedError)
    {
        var service = new FakeWriteService();
        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                [Window(day, startHour, endHour)]));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(service.ReplacedRules);
        Assert.False(service.SaveChangesCalled);
    }

    [Theory]
    [InlineData(9, 12, 9, 12)]
    [InlineData(9, 12, 11, 14)]
    [InlineData(9, 18, 10, 12)]
    public async Task HandleAsync_WithOverlappingWindows_ShouldRejectWithoutWriting(
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd)
    {
        var service = new FakeWriteService();
        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                [
                    Window(1, secondStart, secondEnd),
                    Window(1, firstStart, firstEnd)
                ]));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            AvailabilityRuleOperationError.OverlappingRules,
            result.Error);
        Assert.Null(service.ReplacedRules);
        Assert.False(service.SaveChangesCalled);
    }

        [Theory]
    [InlineData("business",
        AvailabilityRuleOperationError.ProfessionalFromAnotherBusiness)]
    [InlineData("missing-link",
        AvailabilityRuleOperationError.ProfessionalLocationNotFound)]
    [InlineData("inactive-location",
        AvailabilityRuleOperationError.InactiveLocation)]
    [InlineData("inactive-professional",
        AvailabilityRuleOperationError.InactiveProfessional)]
    public async Task HandleAsync_WithInvalidResources_ShouldRejectWithoutWriting(
        string scenario,
        AvailabilityRuleOperationError expectedError)
    {
        var service = new FakeWriteService();

        switch (scenario)
        {
            case "business":
                service.Professional = new Professional(
                    service.Professional.Id,
                    service.Professional.TenantId,
                    Guid.NewGuid(),
                    service.Professional.Name,
                    CreatedAt);
                break;

            case "missing-link":
                service.LinkExists = false;
                break;

            case "inactive-location":
                service.Location.Update(
                    service.Location.Name,
                    service.Location.CountryCode,
                    service.Location.Timezone,
                    false,
                    CreatedAt.AddHours(1));
                break;

            case "inactive-professional":
                service.Professional.Update(
                    service.Professional.Name,
                    false,
                    CreatedAt.AddHours(1));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var handler = new ReplaceAvailabilityRulesHandler(service);

        var result = await handler.HandleAsync(
            new ReplaceAvailabilityRulesCommand(
                service.Location.Id,
                service.Professional.Id,
                [Window(1, 9, 12)]));

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Null(service.ReplacedRules);
        Assert.False(service.SaveChangesCalled);
    }
    private static AvailabilityRuleInput Window(
        int day,
        int startHour,
        int endHour)
    {
        return new AvailabilityRuleInput(
            (DayOfWeek)day,
            new TimeOnly(startHour, 0),
            new TimeOnly(endHour, 0));
    }

    private sealed class FakeWriteService : IAvailabilityRuleWriteService
    {
        public FakeWriteService()
        {
            var tenantId = Guid.NewGuid();
            var businessId = Guid.NewGuid();

            Location = new Location(
                Guid.NewGuid(),
                tenantId,
                businessId,
                "Location",
                "BR",
                "America/Sao_Paulo",
                CreatedAt);

            Professional = new Professional(
                Guid.NewGuid(),
                tenantId,
                businessId,
                "Professional",
                CreatedAt);

            Link = new ProfessionalLocation(
                tenantId,
                Professional.Id,
                Location.Id,
                CreatedAt);
        }

        public Location Location { get; }
        public Professional Professional { get; set; }
        public ProfessionalLocation Link { get; }
        public bool LinkExists { get; set; } = true;
        public Guid? ReplacedLocationId { get; private set; }
        public Guid? ReplacedProfessionalId { get; private set; }
        public IReadOnlyList<AvailabilityRuleInput>? ReplacedRules { get; private set; }
        public DateTimeOffset? ReplacedAt { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task<Location?> GetLocationByIdAsync(
            Guid locationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Location?>(
                Location.Id == locationId ? Location : null);
        }

        public Task<Professional?> GetProfessionalByIdAsync(
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Professional?>(
                Professional.Id == professionalId ? Professional : null);
        }

        public Task<ProfessionalLocation?> GetProfessionalLocationAsync(
            Guid locationId,
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ProfessionalLocation?>(
                LinkExists &&
                Link.LocationId == locationId &&
                Link.ProfessionalId == professionalId
                    ? Link
                    : null);
        }

        public Task ReplaceAsync(
            Guid locationId,
            Guid professionalId,
            IReadOnlyList<AvailabilityRuleInput> rules,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            ReplacedLocationId = locationId;
            ReplacedProfessionalId = professionalId;
            ReplacedRules = rules.ToArray();
            ReplacedAt = updatedAt;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;

            return Task.CompletedTask;
        }
    }
}