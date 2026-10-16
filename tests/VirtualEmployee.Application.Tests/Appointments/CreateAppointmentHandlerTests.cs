using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using Xunit;

namespace VirtualEmployee.Application.Tests.Appointments;

public sealed class CreateAppointmentHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_ShouldNormalizeUtcAndForwardCommand()
    {
        var service = new FakeCreationService();
        var handler = new CreateAppointmentHandler(service);
        var serviceIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var command = CreateCommand() with
        {
            ServiceIds = serviceIds,
            StartsAt = new DateTimeOffset(
                2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3))
        };

        using var cancellation = new CancellationTokenSource();

        var result = await handler.HandleAsync(
            command,
            cancellation.Token);

        Assert.Same(service.Result, result);
        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.ReceivedCommand);

        var received = service.ReceivedCommand;

        Assert.Equal(command.CustomerId, received.CustomerId);
        Assert.Equal(command.LocationId, received.LocationId);
        Assert.Equal(command.ProfessionalId, received.ProfessionalId);
        Assert.Equal(command.IdempotencyKey, received.IdempotencyKey);
        Assert.Equal(command.StartsAt.ToUniversalTime(), received.StartsAt);
        Assert.Equal(TimeSpan.Zero, received.StartsAt.Offset);
        Assert.Equal(serviceIds.ToArray(), received.ServiceIds.ToArray());
        Assert.Equal(cancellation.Token, service.ReceivedCancellationToken);

        serviceIds.Clear();

        Assert.Equal(2, received.ServiceIds.Count);
    }

    [Fact]
    public async Task HandleAsync_WithPastStart_ShouldDelegateForReplayValidation()
    {
        var service = new FakeCreationService();
        var handler = new CreateAppointmentHandler(service);

        var command = CreateCommand() with
        {
            StartsAt = new DateTimeOffset(
                2020, 1, 1, 12, 0, 0, TimeSpan.Zero)
        };

        var result = await handler.HandleAsync(command);

        Assert.Same(service.Result, result);
        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.ReceivedCommand);
        Assert.Equal(command.StartsAt, service.ReceivedCommand.StartsAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("empty-customer")]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("null-services")]
    [InlineData("empty-services")]
    [InlineData("empty-service-id")]
    [InlineData("duplicate-services")]
    [InlineData("missing-start")]
    [InlineData("submicrosecond-start")]
    [InlineData("null-key")]
    [InlineData("empty-key")]
    [InlineData("blank-key")]
    [InlineData("long-key")]
    [InlineData("whitespace-key")]
    [InlineData("control-key")]
    public async Task HandleAsync_WithInvalidRequest_ShouldNotCallService(
        string scenario)
    {
        var service = new FakeCreationService();
        var handler = new CreateAppointmentHandler(service);
        var command = CreateCommand();

        command = scenario switch
        {
            "empty-customer" => command with
            {
                CustomerId = Guid.Empty
            },
            "empty-location" => command with
            {
                LocationId = Guid.Empty
            },
            "empty-professional" => command with
            {
                ProfessionalId = Guid.Empty
            },
            "null-services" => command with
            {
                ServiceIds = null!
            },
            "empty-services" => command with
            {
                ServiceIds = []
            },
            "empty-service-id" => command with
            {
                ServiceIds = [Guid.Empty]
            },
            "duplicate-services" => command with
            {
                ServiceIds = [command.ServiceIds[0], command.ServiceIds[0]]
            },
            "missing-start" => command with
            {
                StartsAt = DateTimeOffset.MinValue
            },
            "submicrosecond-start" => command with
            {
                StartsAt = command.StartsAt.AddTicks(1)
            },
            "null-key" => command with
            {
                IdempotencyKey = null!
            },
            "empty-key" => command with
            {
                IdempotencyKey = ""
            },
            "blank-key" => command with
            {
                IdempotencyKey = "   "
            },
            "long-key" => command with
            {
                IdempotencyKey = new string('a', 201)
            },
            "whitespace-key" => command with
            {
                IdempotencyKey = "booking key"
            },
            "control-key" => command with
            {
                IdempotencyKey = "booking\u0001key"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(CreateAppointmentError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.CallCount);
        Assert.Null(service.ReceivedCommand);
    }

    #endregion

    #region Helpers

    private static CreateAppointmentCommand CreateCommand()
    {
        return new CreateAppointmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [Guid.NewGuid()],
            new DateTimeOffset(
                2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            "appointment-test-key");
    }

    private sealed class FakeCreationService : IAppointmentCreationService
    {
        public int CallCount { get; private set; }

        public CreateAppointmentCommand? ReceivedCommand { get; private set; }

        public CancellationToken ReceivedCancellationToken { get; private set; }

        public CreateAppointmentResult Result { get; } =
            CreateAppointmentResult.Failure(
                CreateAppointmentError.SlotUnavailable);

        public Task<CreateAppointmentResult> CreateAsync(
            CreateAppointmentCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ReceivedCommand = command;
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(Result);
        }
    }

    #endregion
}