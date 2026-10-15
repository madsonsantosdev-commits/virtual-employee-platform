namespace VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

public sealed class CreateScheduleBlockHandler
{
    private readonly IScheduleBlockCreationService _creationService;

    public CreateScheduleBlockHandler(
        IScheduleBlockCreationService creationService)
    {
        _creationService = creationService;
    }

    public Task<CreateScheduleBlockResult> HandleAsync(
        CreateScheduleBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.LocationId == Guid.Empty ||
            command.ProfessionalId == Guid.Empty ||
            command.StartsAt >= command.EndsAt ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return Task.FromResult(
                CreateScheduleBlockResult.Failure(
                    CreateScheduleBlockError.InvalidRequest));
        }

        var normalizedCommand = command with
        {
            StartsAt = command.StartsAt.ToUniversalTime(),
            EndsAt = command.EndsAt.ToUniversalTime()
        };

        return _creationService.CreateAsync(
            normalizedCommand,
            cancellationToken);
    }
}