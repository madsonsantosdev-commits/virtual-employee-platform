using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Application.Tests.Appointments;

public sealed class CancelAppointmentHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_ShouldNormalizeReasonAndForwardCommand()
    {
        var service = new FakeCancellationService();
        var handler = new CancelAppointmentHandler(service);

        var command = CreateCommand() with
        {
            Reason = "  Cliente solicitou  "
        };

        using var source = new CancellationTokenSource();

        var result = await handler.HandleAsync(command, source.Token);

        Assert.Same(service.Result, result);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(source.Token, service.RequestedToken);

        var actual = Assert.IsType<CancelAppointmentCommand>(
            service.RequestedCommand);

        Assert.Equal(command.AppointmentId, actual.AppointmentId);
        Assert.Equal(command.CancellationStatus, actual.CancellationStatus);
        Assert.Equal(command.IdempotencyKey, actual.IdempotencyKey);
        Assert.Equal("Cliente solicitou", actual.Reason);
    }

    [Fact]
    public async Task HandleAsync_WithMaximumKeyLength_ShouldAccept()
    {
        var service = new FakeCancellationService();
        var handler = new CancelAppointmentHandler(service);

        var command = CreateCommand() with
        {
            IdempotencyKey = new string('a', 200)
        };

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CallCount);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public async Task HandleAsync_ShouldValidateNormalizedReasonLength(
        int length)
    {
        var service = new FakeCancellationService();
        var handler = new CancelAppointmentHandler(service);

        var command = CreateCommand() with
        {
            Reason = "  " + new string('a', length) + "  "
        };

        var result = await handler.HandleAsync(command);

        if (length == 500)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(1, service.CallCount);
            Assert.NotNull(service.RequestedCommand);
            Assert.Equal(
                new string('a', length),
                service.RequestedCommand.Reason);
        }
        else
        {
            Assert.Equal(
                CancelAppointmentError.InvalidRequest,
                result.Error);
            Assert.Null(result.Response);
            Assert.Equal(0, service.CallCount);
            Assert.Null(service.RequestedCommand);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public async Task HandleAsync_WithValidOrigin_ShouldCallService(
        AppointmentStatus cancellationStatus)
    {
        var service = new FakeCancellationService();
        var handler = new CancelAppointmentHandler(service);

        var result = await handler.HandleAsync(
            CreateCommand() with
            {
                CancellationStatus = cancellationStatus
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.RequestedCommand);
        Assert.Equal(
            cancellationStatus,
            service.RequestedCommand.CancellationStatus);
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("pending-status")]
    [InlineData("undefined-status")]
    [InlineData("null-reason")]
    [InlineData("empty-reason")]
    [InlineData("blank-reason")]
    [InlineData("null-key")]
    [InlineData("empty-key")]
    [InlineData("blank-key")]
    [InlineData("long-key")]
    [InlineData("key-with-space")]
    [InlineData("key-with-tab")]
    [InlineData("key-with-control")]
    public async Task HandleAsync_WithInvalidCommand_ShouldRejectWithoutCallingService(
        string scenario)
    {
        var service = new FakeCancellationService();
        var handler = new CancelAppointmentHandler(service);
        var valid = CreateCommand();

        var command = scenario switch
        {
            "empty-id" => valid with
            {
                AppointmentId = Guid.Empty
            },
            "pending-status" => valid with
            {
                CancellationStatus = AppointmentStatus.Pending
            },
            "undefined-status" => valid with
            {
                CancellationStatus = (AppointmentStatus)999
            },
            "null-reason" => valid with { Reason = null! },
            "empty-reason" => valid with { Reason = "" },
            "blank-reason" => valid with { Reason = " \t\r\n " },
            "null-key" => valid with { IdempotencyKey = null! },
            "empty-key" => valid with { IdempotencyKey = "" },
            "blank-key" => valid with { IdempotencyKey = "   " },
            "long-key" => valid with
            {
                IdempotencyKey = new string('a', 201)
            },
            "key-with-space" => valid with
            {
                IdempotencyKey = "key with-space"
            },
            "key-with-tab" => valid with
            {
                IdempotencyKey = "key\tvalue"
            },
            "key-with-control" => valid with
            {
                IdempotencyKey = "key\u0001value"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            CancelAppointmentError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.CallCount);
        Assert.Null(service.RequestedCommand);
    }

    [Theory]
    [InlineData(CancelAppointmentError.AppointmentNotFound)]
    [InlineData(CancelAppointmentError.InvalidState)]
    [InlineData(CancelAppointmentError.ReservationExpired)]
    [InlineData(CancelAppointmentError.IdempotencyConflict)]
    [InlineData(CancelAppointmentError.ConcurrencyConflict)]
    public async Task HandleAsync_ShouldPreserveServiceFailure(
        CancelAppointmentError error)
    {
        var expected = CancelAppointmentResult.Failure(error);
        var service = new FakeCancellationService
        {
            Result = expected
        };

        var handler = new CancelAppointmentHandler(service);

        var result = await handler.HandleAsync(CreateCommand());

        Assert.Same(expected, result);
        Assert.Equal(1, service.CallCount);
    }

    #endregion

    #region Helpers

    private static CancelAppointmentCommand CreateCommand()
    {
        return new CancelAppointmentCommand(
            Guid.NewGuid(),
            AppointmentStatus.CancelledByClient,
            "Cliente solicitou",
            Guid.NewGuid().ToString());
    }

    private sealed class FakeCancellationService
        : IAppointmentCancellationService
    {
        public int CallCount { get; private set; }

        public CancelAppointmentCommand? RequestedCommand { get; private set; }

        public CancellationToken RequestedToken { get; private set; }

        public CancelAppointmentResult Result { get; init; } =
            CancelAppointmentResult.Success(
                new CancelAppointmentResponse(
                    Guid.NewGuid(),
                    "CANCELLED_BY_CLIENT",
                    "Cliente solicitou",
                    new DateTimeOffset(
                        2026, 10, 19, 12, 5, 0, TimeSpan.Zero),
                    new DateTimeOffset(
                        2026, 10, 19, 12, 5, 0, TimeSpan.Zero)));

        public Task<CancelAppointmentResult> CancelAsync(
            CancelAppointmentCommand command,
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