using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using Xunit;

namespace VirtualEmployee.Application.Tests.Appointments;

public sealed class RescheduleAppointmentHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_ShouldNormalizeUtcAndForwardCommandAndToken()
    {
        var service = new FakeReschedulingService();
        var handler = new RescheduleAppointmentHandler(service);
        using var source = new CancellationTokenSource();

        var command = CreateCommand() with
        {
            StartsAt = new DateTimeOffset(
                2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(-3))
        };

        var result = await handler.HandleAsync(command, source.Token);

        Assert.Same(service.Result, result);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(source.Token, service.RequestedToken);

        var actual = Assert.IsType<RescheduleAppointmentCommand>(
            service.RequestedCommand);

        Assert.Equal(command.AppointmentId, actual.AppointmentId);
        Assert.Equal(command.LocationId, actual.LocationId);
        Assert.Equal(command.ProfessionalId, actual.ProfessionalId);
        Assert.Equal(command.IdempotencyKey, actual.IdempotencyKey);
        Assert.Equal(
            command.StartsAt.ToUniversalTime(),
            actual.StartsAt);
        Assert.Equal(TimeSpan.Zero, actual.StartsAt.Offset);
    }

    [Fact]
    public async Task HandleAsync_WithMaximumKeyLength_ShouldCallService()
    {
        var service = new FakeReschedulingService();
        var handler = new RescheduleAppointmentHandler(service);

        var command = CreateCommand() with
        {
            IdempotencyKey = new string('a', 200)
        };

        await handler.HandleAsync(command);

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.RequestedCommand);
        Assert.Equal(command.IdempotencyKey, service.RequestedCommand.IdempotencyKey);
    }

    [Fact]
    public async Task HandleAsync_WithPastStart_ShouldDelegateForReplayResolution()
    {
        var service = new FakeReschedulingService();
        var handler = new RescheduleAppointmentHandler(service);

        var command = CreateCommand() with
        {
            StartsAt = new DateTimeOffset(
                2000, 1, 1, 12, 0, 0, TimeSpan.Zero)
        };

        await handler.HandleAsync(command);

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.RequestedCommand);
        Assert.Equal(command.StartsAt, service.RequestedCommand.StartsAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("appointment-id")]
    [InlineData("location-id")]
    [InlineData("professional-id")]
    [InlineData("missing-start")]
    [InlineData("submicrosecond-start")]
    [InlineData("null-key")]
    [InlineData("empty-key")]
    [InlineData("blank-key")]
    [InlineData("long-key")]
    [InlineData("space-in-key")]
    [InlineData("tab-in-key")]
    [InlineData("control-in-key")]
    public async Task HandleAsync_WithInvalidCommand_ShouldRejectWithoutCallingService(
        string scenario)
    {
        var service = new FakeReschedulingService();
        var handler = new RescheduleAppointmentHandler(service);
        var valid = CreateCommand();

        var command = scenario switch
        {
            "appointment-id" => valid with
            {
                AppointmentId = Guid.Empty
            },
            "location-id" => valid with
            {
                LocationId = Guid.Empty
            },
            "professional-id" => valid with
            {
                ProfessionalId = Guid.Empty
            },
            "missing-start" => valid with
            {
                StartsAt = DateTimeOffset.MinValue
            },
            "submicrosecond-start" => valid with
            {
                StartsAt = valid.StartsAt.AddTicks(1)
            },
            "null-key" => valid with
            {
                IdempotencyKey = null!
            },
            "empty-key" => valid with
            {
                IdempotencyKey = ""
            },
            "blank-key" => valid with
            {
                IdempotencyKey = "   "
            },
            "long-key" => valid with
            {
                IdempotencyKey = new string('a', 201)
            },
            "space-in-key" => valid with
            {
                IdempotencyKey = "key value"
            },
            "tab-in-key" => valid with
            {
                IdempotencyKey = "key\tvalue"
            },
            "control-in-key" => valid with
            {
                IdempotencyKey = "key\u0001value"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(command);

        Assert.Equal(
            RescheduleAppointmentError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.CallCount);
        Assert.Null(service.RequestedCommand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(1234560)]
    public async Task HandleAsync_WithMicrosecondPrecision_ShouldPreserveInstant(
        long extraTicks)
    {
        var service = new FakeReschedulingService();
        var handler = new RescheduleAppointmentHandler(service);

        var command = CreateCommand();
        command = command with
        {
            StartsAt = command.StartsAt.AddTicks(extraTicks)
        };

        await handler.HandleAsync(command);

        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.RequestedCommand);
        Assert.Equal(
            command.StartsAt.UtcDateTime.Ticks,
            service.RequestedCommand.StartsAt.UtcDateTime.Ticks);
    }

    [Theory]
    [InlineData(RescheduleAppointmentError.AppointmentNotFound)]
    [InlineData(RescheduleAppointmentError.InvalidState)]
    [InlineData(RescheduleAppointmentError.ReservationExpired)]
    [InlineData(RescheduleAppointmentError.SlotUnavailable)]
    [InlineData(RescheduleAppointmentError.IdempotencyConflict)]
    [InlineData(RescheduleAppointmentError.ConcurrencyConflict)]
    public async Task HandleAsync_ShouldPreserveServiceResult(
        RescheduleAppointmentError error)
    {
        var expected = RescheduleAppointmentResult.Failure(error);
        var service = new FakeReschedulingService
        {
            Result = expected
        };

        var handler = new RescheduleAppointmentHandler(service);

        var result = await handler.HandleAsync(CreateCommand());

        Assert.Same(expected, result);
        Assert.Equal(1, service.CallCount);
    }

    #endregion

    #region Helpers

    private static RescheduleAppointmentCommand CreateCommand()
    {
        return new RescheduleAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(
                2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
            Guid.NewGuid().ToString());
    }

    private sealed class FakeReschedulingService
        : IAppointmentReschedulingService
    {
        public int CallCount { get; private set; }

        public RescheduleAppointmentCommand? RequestedCommand
        {
            get;
            private set;
        }

        public CancellationToken RequestedToken { get; private set; }

        public RescheduleAppointmentResult Result { get; init; } =
            RescheduleAppointmentResult.Failure(
                RescheduleAppointmentError.SlotUnavailable);

        public Task<RescheduleAppointmentResult> RescheduleAsync(
            RescheduleAppointmentCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedCommand = command;
            RequestedToken = cancellationToken;

            return Task.FromResult(Result);
        }
    }

    #endregion
}