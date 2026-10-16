using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;
using Xunit;

namespace VirtualEmployee.Application.Tests.ScheduleBlocks;

public sealed class CreateScheduleBlockHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_ShouldAcceptMaximumKeyLength()
    {
        var service = new FakeCreationService();
        var handler = new CreateScheduleBlockHandler(service);
        var command = CreateCommand() with
        {
            IdempotencyKey = new string('A', 200)
        };

        var result = await handler.HandleAsync(command);

        Assert.Same(service.Result, result);
        Assert.NotNull(service.Command);
        Assert.Equal(command.IdempotencyKey, service.Command.IdempotencyKey);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task HandleAsync_ShouldPreserveReplayResult()
    {
        var response = new ScheduleBlockResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var service = new FakeCreationService
        {
            Result = CreateScheduleBlockResult.Success(
                response,
                isReplay: true)
        };

        var handler = new CreateScheduleBlockHandler(service);

        var result = await handler.HandleAsync(CreateCommand());

        Assert.Same(service.Result, result);
        Assert.True(result.IsSuccess);
        Assert.True(result.IsReplay);
        Assert.Same(response, result.Response);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_ShouldNormalizeInstantsAndPreserveScope(
        bool professionalScoped)
    {
        var service = new FakeCreationService();
        var handler = new CreateScheduleBlockHandler(service);
        var command = CreateCommand() with
        {
            ProfessionalId = professionalScoped ? Guid.NewGuid() : null
        };

        using var cancellationSource = new CancellationTokenSource();

        var result = await handler.HandleAsync(
            command,
            cancellationSource.Token);

        Assert.Same(service.Result, result);
        Assert.Equal(1, service.CallCount);
        Assert.NotNull(service.Command);
        Assert.Equal(command.LocationId, service.Command.LocationId);
        Assert.Equal(command.ProfessionalId, service.Command.ProfessionalId);
        Assert.Equal(command.IdempotencyKey, service.Command.IdempotencyKey);
        Assert.Equal(command.StartsAt.ToUniversalTime(), service.Command.StartsAt);
        Assert.Equal(command.EndsAt.ToUniversalTime(), service.Command.EndsAt);
        Assert.Equal(TimeSpan.Zero, service.Command.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, service.Command.EndsAt.Offset);
        Assert.Equal(cancellationSource.Token, service.CancellationToken);
    }

    [Theory]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("equal-times")]
    [InlineData("reversed-times")]
    [InlineData("null-key")]
    [InlineData("empty-key")]
    [InlineData("blank-key")]
    [InlineData("long-key")]
    [InlineData("space-in-key")]
    [InlineData("control-in-key")]
    public async Task HandleAsync_WithInvalidRequest_ShouldNotCallService(
        string scenario)
    {
        var service = new FakeCreationService();
        var handler = new CreateScheduleBlockHandler(service);
        var command = CreateCommand();

        command = scenario switch
        {
            "empty-location" => command with
            {
                LocationId = Guid.Empty
            },
            "empty-professional" => command with
            {
                ProfessionalId = Guid.Empty
            },
            "equal-times" => command with
            {
                EndsAt = command.StartsAt
            },
            "reversed-times" => command with
            {
                EndsAt = command.StartsAt.AddMinutes(-1)
            },
            "null-key" => command with
            {
                IdempotencyKey = null!
            },
            "empty-key" => command with
            {
                IdempotencyKey = string.Empty
            },
            "blank-key" => command with
            {
                IdempotencyKey = "   "
            },
            "long-key" => command with
            {
                IdempotencyKey = new string('a', 201)
            },
            "space-in-key" => command with
            {
                IdempotencyKey = "key with space"
            },
            "control-in-key" => command with
            {
                IdempotencyKey = "key\u0001"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(command);

        Assert.False(result.IsSuccess);
        Assert.Equal(CreateScheduleBlockError.InvalidRequest, result.Error);
        Assert.Null(result.Response);
        Assert.False(result.IsReplay);
        Assert.Equal(0, service.CallCount);
        Assert.Null(service.Command);
    }

    #endregion

    #region Helpers

    private static CreateScheduleBlockCommand CreateCommand()
    {
        var start = new DateTimeOffset(
            2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3));

        return new CreateScheduleBlockCommand(
            Guid.NewGuid(),
            null,
            start,
            start.AddHours(1),
            "Block-Key-ABC");
    }

    private sealed class FakeCreationService : IScheduleBlockCreationService
    {
        public int CallCount { get; private set; }
        public CreateScheduleBlockCommand? Command { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public CreateScheduleBlockResult Result { get; set; } =
            CreateScheduleBlockResult.Failure(
                CreateScheduleBlockError.LocationNotFound);

        public Task<CreateScheduleBlockResult> CreateAsync(
            CreateScheduleBlockCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Command = command;
            CancellationToken = cancellationToken;

            return Task.FromResult(Result);
        }
    }

    #endregion
}