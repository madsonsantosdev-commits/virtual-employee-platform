using VirtualEmployee.Domain.Scheduling;

namespace VirtualEmployee.Application.Appointments.CancelAppointment;

public sealed class CancelAppointmentHandler
{
    private readonly IAppointmentCancellationService _cancellationService;

    public CancelAppointmentHandler(
        IAppointmentCancellationService cancellationService)
    {
        _cancellationService = cancellationService;
    }

    public Task<CancelAppointmentResult> HandleAsync(
        CancelAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AppointmentId == Guid.Empty ||
            command.CancellationStatus is not
                (AppointmentStatus.CancelledByClient or
                 AppointmentStatus.CancelledByBusiness) ||
            string.IsNullOrWhiteSpace(command.Reason) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Task.FromResult(
                CancelAppointmentResult.Failure(
                    CancelAppointmentError.InvalidRequest));
        }

        if (command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return Task.FromResult(
                CancelAppointmentResult.Failure(
                    CancelAppointmentError.InvalidRequest));
        }

                if (command.Reason.Trim().Length > 500)
        {
            return Task.FromResult(
                CancelAppointmentResult.Failure(
                    CancelAppointmentError.InvalidRequest));
        }

        var normalizedCommand = command with
        {
            Reason = command.Reason.Trim()
        };

        return _cancellationService.CancelAsync(
            normalizedCommand,
            cancellationToken);
    }
}