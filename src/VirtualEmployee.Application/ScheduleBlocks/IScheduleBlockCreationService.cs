using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

namespace VirtualEmployee.Application.ScheduleBlocks;

public interface IScheduleBlockCreationService
{
    Task<CreateScheduleBlockResult> CreateAsync(
        CreateScheduleBlockCommand command,
        CancellationToken cancellationToken = default);
}