namespace VirtualEmployee.Domain.Payments;

public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Confirmed = 3,
    Failed = 4,
    Cancelled = 5,
    Refunded = 6
}