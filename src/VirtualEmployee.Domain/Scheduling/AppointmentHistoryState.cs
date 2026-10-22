namespace VirtualEmployee.Domain.Scheduling;

/// <summary>
/// Estado operacional capturado para o histórico do agendamento.
/// Não inclui dados pessoais nem duplica os itens comerciais.
/// </summary>
public sealed record AppointmentHistoryState(
    AppointmentStatus Status,
    Guid LocationId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset? ReservationExpiresAt)
{
    public static AppointmentHistoryState Capture(
        Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new AppointmentHistoryState(
            appointment.Status,
            appointment.LocationId,
            appointment.ProfessionalId,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.ReservationExpiresAt);
    }
}