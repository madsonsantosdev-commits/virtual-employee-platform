namespace VirtualEmployee.Application.Appointments;

public sealed class AppointmentReservationOptions
{
    public const string SectionName = "Scheduling:Reservations";

    public int LifetimeMinutes { get; set; } = 10;

    public bool ExpirationWorkerEnabled { get; set; }

    public int ExpirationIntervalSeconds { get; set; } = 30;
}