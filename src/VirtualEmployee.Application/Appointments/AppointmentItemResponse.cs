namespace VirtualEmployee.Application.Appointments;

public sealed record AppointmentItemResponse(
    Guid ServiceId,
    string Name,
    decimal Price,
    int DurationMinutes);