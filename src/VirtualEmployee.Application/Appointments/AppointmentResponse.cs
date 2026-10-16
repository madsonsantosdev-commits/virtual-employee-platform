namespace VirtualEmployee.Application.Appointments;

public sealed record AppointmentResponse(
    Guid Id,
    Guid BusinessId,
    Guid LocationId,
    Guid CustomerId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    decimal TotalPrice,
    int TotalDurationMinutes,
    DateTimeOffset? ReservationExpiresAt,
    IReadOnlyList<AppointmentItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);