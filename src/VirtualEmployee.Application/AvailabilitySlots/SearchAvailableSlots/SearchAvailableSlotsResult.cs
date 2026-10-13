namespace VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

public sealed record SearchAvailableSlotsResult(
    SearchAvailableSlotsError Error,
    SearchAvailableSlotsResponse? Response)
{
    public bool IsSuccess => Error == SearchAvailableSlotsError.None;

    public static SearchAvailableSlotsResult Success(
        SearchAvailableSlotsResponse response)
        => new(SearchAvailableSlotsError.None, response);

    public static SearchAvailableSlotsResult Failure(
        SearchAvailableSlotsError error)
        => new(error, null);
}