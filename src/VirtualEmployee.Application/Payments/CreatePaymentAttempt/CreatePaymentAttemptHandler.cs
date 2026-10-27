namespace VirtualEmployee.Application.Payments.CreatePaymentAttempt;

public sealed class CreatePaymentAttemptHandler
{
    private readonly ICreatePaymentAttemptService _service;

    public CreatePaymentAttemptHandler(
        ICreatePaymentAttemptService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public async Task<CreatePaymentAttemptResult> HandleAsync(
        CreatePaymentAttemptCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AppointmentId == Guid.Empty ||
            !Enum.IsDefined(command.PaymentMethod) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Length > 200 ||
            command.IdempotencyKey.Any(character =>
                char.IsWhiteSpace(character) ||
                char.IsControl(character)))
        {
            return CreatePaymentAttemptResult.Failure(
                CreatePaymentAttemptError.InvalidRequest);
        }

        return await _service.CreateAsync(
            command,
            cancellationToken);
    }
}