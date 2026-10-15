namespace VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

public sealed record CreateScheduleBlockResult(
    CreateScheduleBlockError Error,
    ScheduleBlockResponse? Response,
    bool IsReplay)
{
    public bool IsSuccess => Error == CreateScheduleBlockError.None;

    public static CreateScheduleBlockResult Success(
        ScheduleBlockResponse response,
        bool isReplay = false)
        => new(CreateScheduleBlockError.None, response, isReplay);

    public static CreateScheduleBlockResult Failure(
        CreateScheduleBlockError error)
        => new(error, null, false);
}