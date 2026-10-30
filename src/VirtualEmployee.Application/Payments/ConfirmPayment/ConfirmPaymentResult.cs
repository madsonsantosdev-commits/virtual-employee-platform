namespace VirtualEmployee.Application.Payments.ConfirmPayment;

public sealed record ConfirmPaymentResult(
    ConfirmPaymentOutcome Outcome,
    Guid? PaymentId,
    Guid? AppointmentId)
{
    public bool RequiresCompensation =>
        Outcome is
            ConfirmPaymentOutcome.ReservationExpired or
            ConfirmPaymentOutcome.AppointmentNotConfirmable or
            ConfirmPaymentOutcome.AdditionalPayment;
}