namespace VirtualEmployee.Domain.Payments;

public enum RefundStatus
{
    Requested = 1,
    Processing = 2,
    Refunded = 3,
    Failed = 4
}
