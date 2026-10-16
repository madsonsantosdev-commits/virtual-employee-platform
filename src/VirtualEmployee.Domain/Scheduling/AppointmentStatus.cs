namespace VirtualEmployee.Domain.Scheduling;

public enum AppointmentStatus
{
    Pending = 1,
    Confirmed = 2,
    ConfirmedByClient = 3,
    RescheduleRequested = 4,
    CancelledByClient = 5,
    CancelledByBusiness = 6,
    Completed = 7,
    NoShow = 8,
    Expired = 9
}