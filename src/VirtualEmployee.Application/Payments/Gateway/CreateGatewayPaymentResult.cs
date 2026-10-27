namespace VirtualEmployee.Application.Payments.Gateway;

public sealed record CreateGatewayPaymentResult
{
    public CreateGatewayPaymentOutcome Outcome { get; }
    public GatewayPaymentReference? Reference { get; }

    private CreateGatewayPaymentResult(
        CreateGatewayPaymentOutcome outcome,
        GatewayPaymentReference? reference)
    {
        Outcome = outcome;
        Reference = reference;
    }

    public static CreateGatewayPaymentResult Created(
        GatewayPaymentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new CreateGatewayPaymentResult(
            CreateGatewayPaymentOutcome.Created,
            reference);
    }

    public static CreateGatewayPaymentResult Rejected()
    {
        return new CreateGatewayPaymentResult(
            CreateGatewayPaymentOutcome.Rejected,
            null);
    }

    public static CreateGatewayPaymentResult Unknown()
    {
        return new CreateGatewayPaymentResult(
            CreateGatewayPaymentOutcome.Unknown,
            null);
    }
}