namespace VirtualEmployee.Application.Appointments.CancelAppointment;

public enum CancelAppointmentError
{
    None,
    InvalidRequest,
    AppointmentNotFound,
    InvalidState,
    ReservationExpired,
    IdempotencyConflict,
    ConcurrencyConflict
}