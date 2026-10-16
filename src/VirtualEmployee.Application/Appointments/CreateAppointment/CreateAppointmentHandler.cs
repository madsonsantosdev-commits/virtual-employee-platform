namespace VirtualEmployee.Application.Appointments.CreateAppointment;

public sealed class CreateAppointmentHandler
{
    private readonly IAppointmentCreationService _creationService;

    public CreateAppointmentHandler(
        IAppointmentCreationService creationService)
    {
        _creationService = creationService;
    }

    public Task<CreateAppointmentResult> HandleAsync(
        CreateAppointmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CustomerId == Guid.Empty ||
            command.LocationId == Guid.Empty ||
            command.ProfessionalId == Guid.Empty ||
            command.ServiceIds is null ||
            command.ServiceIds.Count == 0 ||
            command.StartsAt == DateTimeOffset.MinValue ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return InvalidRequest();
        }

        var serviceIds = command.ServiceIds.ToArray();

        if (serviceIds.Any(id => id == Guid.Empty) ||
            serviceIds.Distinct().Count() != serviceIds.Length)
        {
            return InvalidRequest();
        }

        var startsAtUtc = command.StartsAt.ToUniversalTime();

        // PostgreSQL preserva microssegundos.
        // Rejeita precisão adicional para não alterar o instante solicitado.
        if (startsAtUtc.Ticks % 10 != 0)
        {
            return InvalidRequest();
        }

        var normalizedCommand = command with
        {
            StartsAt = startsAtUtc,
            ServiceIds = serviceIds
        };

        return _creationService.CreateAsync(
            normalizedCommand,
            cancellationToken);
    }

    private static Task<CreateAppointmentResult> InvalidRequest()
    {
        return Task.FromResult(
            CreateAppointmentResult.Failure(
                CreateAppointmentError.InvalidRequest));
    }
}