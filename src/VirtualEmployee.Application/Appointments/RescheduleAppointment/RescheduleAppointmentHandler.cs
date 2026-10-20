namespace VirtualEmployee.Application.Appointments.RescheduleAppointment;

public sealed class RescheduleAppointmentHandler
{
    private readonly IAppointmentReschedulingService _reschedulingService;

    public RescheduleAppointmentHandler(
        IAppointmentReschedulingService reschedulingService)
    {
        _reschedulingService = reschedulingService;
    }

    public Task<RescheduleAppointmentResult> HandleAsync(
        RescheduleAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AppointmentId == Guid.Empty ||
            command.LocationId == Guid.Empty ||
            command.ProfessionalId == Guid.Empty ||
            command.StartsAt == DateTimeOffset.MinValue ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return InvalidRequest();
        }

        if (command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return InvalidRequest();
        }

        var utcStartsAt = command.StartsAt.ToUniversalTime();

        // PostgreSQL armazena os instantes com precisão de microssegundos.
        // Rejeita precisão excedente em vez de arredondar o pedido.
        if (utcStartsAt.Ticks % 10 != 0)
        {
            return InvalidRequest();
        }

        var normalizedCommand = command with
        {
            StartsAt = utcStartsAt
        };

        return _reschedulingService.RescheduleAsync(
            normalizedCommand,
            cancellationToken);
    }

    private static Task<RescheduleAppointmentResult> InvalidRequest()
    {
        return Task.FromResult(
            RescheduleAppointmentResult.Failure(
                RescheduleAppointmentError.InvalidRequest));
    }
}